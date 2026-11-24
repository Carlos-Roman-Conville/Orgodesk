using System.Text.Json.Nodes;
using Orgodesk.Core.Persistence;

namespace Orgodesk.Commands.Tests;

/// <summary>
/// FC-57, HQ BASELINE sections 6 and 7 (data-safety-tests): command mode keeps the person's layout whole through a
/// damaged file, a half-written file left by a crash, a file the disk will not take, and data from the last release.
/// </summary>
public class DataSafetyTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Data", "v1.0.1");

    /// <summary>A desk whose data folder holds what v1.0.1 wrote (layout.json, settings.json, a snapshot).</summary>
    private static Desk FromRelease()
    {
        var d = new Desk();
        File.Copy(Path.Combine(Fixture, "layout.json"), d.Store.LayoutPath, overwrite: true);
        File.Copy(Path.Combine(Fixture, "settings.json"), d.Store.SettingsPath, overwrite: true);
        File.Copy(Path.Combine(Fixture, "snapshots", "Monday.json"), Path.Combine(d.Store.SnapshotsDir, "Monday.json"), overwrite: true);
        return d;
    }

    [Fact]
    public void Data_written_by_v1_0_1_opens_lists_and_takes_every_kind_of_change()
    {
        using var d = FromRelease();
        var groups = d.Ask("list", "groups")["result"]!.AsArray();
        Assert.Equal(new[] { "Projects", "Games", "Downloads", "Second" }, groups.Select(g => g!["name"]!.GetValue<string>()));
        Assert.Equal(@"C:\Users\tester\Downloads", groups[2]!["folder"]!.GetValue<string>());
        Assert.True(groups[1]!["rolledUp"]!.GetValue<bool>());
        Assert.Equal("Work", d.Ask("list", "presets")["result"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("Monday", d.Ask("list", "snapshots")["result"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(2, d.Ask("list", "rules")["result"]!.AsArray().Count);
        var status = d.Ask("status")["result"]!["tool"]!;
        Assert.Equal("ok", status["layout"]!.GetValue<string>());
        Assert.Equal(4, status["groups"]!.GetValue<int>());                       // across both pages

        Assert.True(d.Do("rename_group", new JsonObject { ["group"] = "Games", ["name"] = "Play" }).Apply["ok"]!.GetValue<bool>());
        Assert.True(d.Do("move_icon", new JsonObject { ["icon"] = "notes", ["to"] = "Projects" }).Apply["ok"]!.GetValue<bool>());
        Assert.True(d.Do("preset", new JsonObject { ["name"] = "Work" }).Apply["ok"]!.GetValue<bool>());
        Assert.True(d.Do("snapshot_restore", new JsonObject { ["name"] = "Monday" }).Apply["ok"]!.GetValue<bool>());
        var layout = d.Layout;
        Assert.Equal(2, layout.Pages.Count);                                    // the second page survived every change
        Assert.Equal(1.5, layout.GridScale);
        Assert.Equal(2, layout.Rules.Count);
        Assert.Contains(layout.AllFences, f => f.Title == "Games" && f.AutoRollUp);   // the snapshot put the old state back whole
    }

    [Fact]
    public void A_half_written_temp_file_beside_a_good_layout_changes_nothing_and_the_next_apply_overwrites_it()
    {
        using var d = new Desk();
        string tmp = d.Store.LayoutPath + ".tmp";
        File.WriteAllText(tmp, "{ \"version\": 1, \"pages\": [ {");               // what a crash between write and rename leaves
        string good = File.ReadAllText(d.Store.LayoutPath);
        Assert.True(d.Ask("list", "groups")["ok"]!.GetValue<bool>());
        Assert.Equal(good, File.ReadAllText(d.Store.LayoutPath));
        Assert.True(d.Do("new_group", new JsonObject { ["name"] = "School" }).Apply["ok"]!.GetValue<bool>());
        Assert.False(File.Exists(tmp));
        Assert.Contains(d.Layout.AllFences, f => f.Title == "School");
    }

    [Fact]
    public void A_layout_the_disk_will_not_take_is_a_plain_refusal_and_the_layout_is_as_it_was()
    {
        using var d = new Desk();
        var p = d.Ask("preview", "new_group", new JsonObject { ["name"] = "School" });
        string good = File.ReadAllText(d.Store.LayoutPath);
        File.SetAttributes(d.Store.LayoutPath, FileAttributes.ReadOnly);
        try
        {
            var apply = d.Ask("apply", previewId: Desk.Id(p));
            Assert.Equal("failed", Desk.Error(apply));
            string msg = Desk.Message(apply);
            Assert.Contains("layout was not changed", msg);
            Assert.DoesNotContain("Exception", msg);
            Assert.DoesNotContain(d.Root, msg);                                   // no path leaks into the answer
            Assert.Equal(good, File.ReadAllText(d.Store.LayoutPath));
            Assert.False(File.Exists(d.Store.LayoutPath + ".tmp"));              // FC-31: no stray temp file
        }
        finally { File.SetAttributes(d.Store.LayoutPath, FileAttributes.Normal); }
    }

    [Fact]
    public void A_damaged_layout_is_refused_by_every_verb_and_left_for_the_app_to_restore_from_its_backup()
    {
        using var d = new Desk();
        File.Copy(d.Store.LayoutPath, d.Store.LayoutBackupPath);                   // what the app keeps after a clean load
        File.WriteAllBytes(d.Store.LayoutPath, new byte[] { 0x7b, 0x22, 0x70, 0xff, 0xfe, 0x00 });
        var damaged = File.ReadAllBytes(d.Store.LayoutPath);
        foreach (var (verb, action, args) in new (string, string?, JsonObject?)[] {
            ("list", "groups", null), ("list", "rules", null), ("preview", "new_group", new JsonObject { ["name"] = "X" }),
            ("preview", "roll_up", new JsonObject { ["group"] = "Projects" }) })
            Assert.Equal("failed", Desk.Error(d.Ask(verb, action, args)));
        Assert.Equal("damaged", d.Ask("status")["result"]!["tool"]!["layout"]!.GetValue<string>());
        Assert.Equal(damaged, File.ReadAllBytes(d.Store.LayoutPath));              // not copied aside, not repaired: the app's job at its next start
        Assert.Empty(Directory.GetFiles(d.Data, "*.corrupt-*"));
        Assert.Contains("restores it from its backup", Desk.Message(d.Ask("list", "groups")));
    }

    [Fact]
    public void A_missing_layout_is_an_empty_desktop_and_a_change_creates_it()
    {
        using var d = new Desk();
        File.Delete(d.Store.LayoutPath);
        Assert.Empty(d.Ask("list", "groups")["result"]!.AsArray());
        Assert.False(File.Exists(d.Store.LayoutPath));                           // looking writes nothing
        Assert.True(d.Do("new_group", new JsonObject { ["name"] = "School" }).Apply["ok"]!.GetValue<bool>());
        Assert.Single(d.Layout.AllFences);
    }

    [Fact]
    public void Looking_in_command_mode_writes_no_file_at_all()
    {
        using var d = new Desk();
        var before = d.Files();
        foreach (var a in new[] { "groups", "presets", "snapshots", "rules" }) Assert.True(d.Ask("list", a)["ok"]!.GetValue<bool>());
        Assert.True(d.Ask("status")["ok"]!.GetValue<bool>());
        Assert.Equal(before, d.Files());
    }
}
