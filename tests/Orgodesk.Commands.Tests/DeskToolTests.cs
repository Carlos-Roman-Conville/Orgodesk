using System.Text.Json.Nodes;
using Orgo.Commands;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Rules;

namespace Orgodesk.Commands.Tests;

/// <summary>FC-55: Orgodesk's command actions through orgo-commands' CommandHost, on a generated desktop.</summary>
public class DeskToolTests
{
    private static string Says(JsonNode preview) =>
        string.Join("\n", preview["result"]!["items"]!.AsArray().Select(i => i!["text"]!.GetValue<string>()));

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    [Fact]
    public void The_catalogue_its_usage_text_and_test_requests_pass_the_contract_check()
    {
        using var d = new Desk();
        var rules = new ContractRules
        {
            NoInjection = new[] { "presets", "snapshots", "rules", "new_group", "add_rule", "quick_hide", "preset", "save_preset", "portal", "roll_up", "snapshot_save", "snapshot_restore" },
        };
        Assert.Empty(ContractCheck.Problems(d.Tool.Info, Path.Combine(RepoRoot(), "tests", "commands", "requests.jsonl"), rules));
    }

    [Fact]
    public void Lists_say_what_their_usage_text_says()
    {
        using var d = new Desk(l => l.Rules.Add(new Rule { Name = "PDFs", Kind = RuleKind.Extension, Pattern = "pdf", TargetFenceId = "work" }));
        var groups = d.Ask("list", "groups")["result"]!.AsArray();
        Assert.Equal(new[] { "Projects", "Work" }, groups.Select(g => g!["name"]!.GetValue<string>()));
        Assert.Equal("plan.docx", groups[0]!["icons"]![0]!.GetValue<string>());
        Assert.Equal("Gaming", d.Ask("list", "presets")["result"]![0]!["name"]!.GetValue<string>());
        var rule = d.Ask("list", "rules")["result"]![0]!;
        Assert.Equal("Work", rule["group"]!.GetValue<string>());
        Assert.Equal("files ending in .pdf", rule["matches"]!.GetValue<string>());
        Assert.Empty(d.Ask("list", "snapshots")["result"]!.AsArray());
        var status = d.Ask("status")["result"]!["tool"]!;
        Assert.Equal(2, status["groups"]!.GetValue<int>());
        Assert.False(status["running"]!.GetValue<bool>());
    }

    public static IEnumerable<object[]> Previews() => new[]
    {
        new object[] { "new_group", """{"name":"School"}""" },
        new object[] { "add_rule", """{"match":{"ext":["pdf"]},"group":"Work"}""" },
        new object[] { "preset", """{"name":"Gaming"}""" },
        new object[] { "save_preset", """{"name":"Office"}""" },
        new object[] { "portal", """{"folder":"Downloads"}""" },
        new object[] { "portal", """{"folder":"Downloads\\Documents","group":"Projects"}""" },
        new object[] { "roll_up", """{"group":"Projects"}""" },
        new object[] { "snapshot_save", """{"name":"Before"}""" },
    };

    [Theory]
    [MemberData(nameof(Previews))]
    public void A_preview_changes_nothing(string action, string args)
    {
        using var d = new Desk();
        var before = d.Files();
        var p = d.Ask("preview", action, JsonNode.Parse(args)!.AsObject());
        Assert.True(p["ok"]!.GetValue<bool>(), p.ToJsonString());
        Assert.Equal(before, d.Files());
    }

    [Fact]
    public void New_group_applies_and_its_undo_snapshot_puts_the_layout_back()
    {
        using var d = new Desk();
        string was = File.ReadAllText(d.Store.LayoutPath);
        var (_, apply) = d.Do("new_group", new JsonObject { ["name"] = "School" });
        Assert.True(apply["ok"]!.GetValue<bool>(), apply.ToJsonString());
        var school = d.Layout.AllFences.Single(f => f.Title == "School");
        Assert.Empty(school.Items);
        Assert.DoesNotContain(d.Layout.AllFences.Where(f => f.Title != "School"), f => f.Bounds.IntersectsWith(school.Bounds));
        string undo = apply["result"]!["tool"]!["undo"]!.GetValue<string>();
        string name = undo.Split('"')[1];
        Assert.StartsWith(DeskTool.BeforePrefix, name);
        Assert.False(apply["result"]!["tool"]!["shownNow"]!.GetValue<bool>());     // no app running in the test

        var (p, back) = d.Do("snapshot_restore", new JsonObject { ["name"] = name });
        Assert.Contains("Groups not in it go away", Says(p));
        Assert.True(back["ok"]!.GetValue<bool>(), back.ToJsonString());
        Assert.DoesNotContain(d.Layout.AllFences, f => f.Title == "School");
        Assert.Equal(System.Text.Json.Nodes.JsonNode.Parse(was)!.ToJsonString(), System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(d.Store.LayoutPath))!.ToJsonString());
    }

    [Fact]
    public void Add_rule_then_sort_icons_moves_exactly_the_icons_shown()
    {
        using var d = new Desk();
        Assert.True(d.Do("add_rule", new JsonObject { ["match"] = new JsonObject { ["ext"] = new JsonArray("pdf", ".PDF") }, ["group"] = "work" }).Apply["ok"]!.GetValue<bool>());
        var rule = d.Layout.Rules.Single();
        Assert.Equal("pdf", rule.Pattern);
        Assert.Equal("work", rule.TargetFenceId);
        Assert.Empty(d.Layout.FindFence("work")!.Items);                          // a rule alone moves nothing

        var (p, apply) = d.Do("sort_icons");
        Assert.Contains("Moves 2 icons into \"Work\": \"a.pdf\", \"b.pdf\".", Says(p));
        Assert.True(apply["ok"]!.GetValue<bool>(), apply.ToJsonString());
        Assert.Equal(new[] { d.Key("a.pdf"), d.Key("b.pdf") }, d.Layout.FindFence("work")!.Items);
        Assert.False(d.Layout.ActivePage.FreeItems.ContainsKey(d.Key("a.pdf")));
        Assert.Equal(new[] { d.Key("plan.docx") }, d.Layout.FindFence("projects")!.Items);
    }

    [Fact]
    public void Apply_refuses_when_the_desktop_changed_since_the_preview_and_changes_nothing()
    {
        using var d = new Desk(l => l.Rules.Add(new Rule { Name = "PDFs", Kind = RuleKind.Extension, Pattern = "pdf", TargetFenceId = "work" }));
        var p = d.Ask("preview", "sort_icons");
        d.Items.Add(new ItemFacts(d.Key("c.pdf"), "c.pdf", ".pdf", false, false, false));   // a new pdf arrives
        string before = File.ReadAllText(d.Store.LayoutPath);
        var apply = d.Ask("apply", previewId: Desk.Id(p));
        Assert.Equal("refused", Desk.Error(apply));
        Assert.Contains("changed since the preview", Desk.Message(apply));
        Assert.Equal(before, File.ReadAllText(d.Store.LayoutPath));
        Assert.Empty(d.Store.ListSnapshots());
    }

    [Fact]
    public void Sort_icons_refuses_when_the_desktop_cannot_be_read_or_there_are_no_rules()
    {
        using var d = new Desk();
        Assert.Equal("not_found", Desk.Error(d.Ask("preview", "sort_icons")));
        d.Tool.GetType();
        using var r = new Desk(l => l.Rules.Add(new Rule { Kind = RuleKind.Extension, Pattern = "pdf", TargetFenceId = "work" }));
        r.DesktopUnreadable = true;
        Assert.Equal("failed", Desk.Error(r.Ask("preview", "sort_icons")));
        r.DesktopUnreadable = false;
        r.Items.Clear();                                                         // FC-51: empty while icons are placed is not the truth
        Assert.Equal("failed", Desk.Error(r.Ask("preview", "sort_icons")));
    }

    [Fact]
    public void Preset_save_preset_roll_up_and_snapshot_save_apply()
    {
        using var d = new Desk();
        Assert.True(d.Do("save_preset", new JsonObject { ["name"] = "Office" }).Apply["ok"]!.GetValue<bool>());
        Assert.Contains(d.Layout.Presets, p => p.Name == "Office" && p.Fences.Count == 2);

        var (p, apply) = d.Do("preset", new JsonObject { ["name"] = "gaming" });
        Assert.Contains("1 group (\"Games\")", Says(p));
        Assert.True(apply["ok"]!.GetValue<bool>(), apply.ToJsonString());
        Assert.Equal(new[] { "Games" }, d.Layout.ActivePage.Fences.Select(f => f.Title));

        Assert.True(d.Do("preset", new JsonObject { ["name"] = "Office" }).Apply["ok"]!.GetValue<bool>());
        Assert.True(d.Do("roll_up", new JsonObject { ["group"] = "Projects" }).Apply["ok"]!.GetValue<bool>());
        Assert.True(d.Layout.AllFences.Single(f => f.Title == "Projects").RolledUp);
        Assert.Equal("refused", Desk.Error(d.Ask("preview", "roll_up", new JsonObject { ["group"] = "Projects" })));
        Assert.True(d.Do("roll_up", new JsonObject { ["group"] = "Projects", ["open"] = true }).Apply["ok"]!.GetValue<bool>());
        Assert.False(d.Layout.AllFences.Single(f => f.Title == "Projects").RolledUp);

        string layout = File.ReadAllText(d.Store.LayoutPath);
        Assert.True(d.Do("snapshot_save", new JsonObject { ["name"] = "Before" }).Apply["ok"]!.GetValue<bool>());
        Assert.NotNull(d.Store.LoadSnapshot("Before"));
        Assert.Equal(layout, File.ReadAllText(d.Store.LayoutPath));               // the desktop itself does not change
        Assert.Equal("refused", Desk.Error(d.Ask("preview", "snapshot_save", new JsonObject { ["name"] = "before" })));   // never replaced
    }

    [Fact]
    public void Portal_makes_a_folder_group_or_turns_a_group_into_one_and_its_icons_go_back_to_the_desktop()
    {
        using var d = new Desk();
        Assert.True(d.Do("portal", new JsonObject { ["folder"] = "downloads" }).Apply["ok"]!.GetValue<bool>());
        Assert.Contains(d.Layout.AllFences, f => f.Title == "Downloads" && f.PortalPath == d.Downloads);
        Assert.Equal("refused", Desk.Error(d.Ask("preview", "portal", new JsonObject { ["folder"] = d.Downloads })));

        var (p, apply) = d.Do("portal", new JsonObject { ["folder"] = "Downloads\\Documents", ["group"] = "Projects" });
        Assert.Contains("\"plan.docx\"", Says(p));
        Assert.True(apply["ok"]!.GetValue<bool>(), apply.ToJsonString());
        var projects = d.Layout.FindFence("projects")!;
        Assert.Equal(Path.Combine(d.Downloads, "Documents"), projects.PortalPath);
        Assert.Empty(projects.Items);
        Assert.True(d.Layout.ActivePage.FreeItems.ContainsKey(d.Key("plan.docx")));
    }

    [Theory]
    [InlineData("portal", """{"folder":"Nowhere"}""", "not_found")]
    [InlineData("portal", """{"folder":"Downloads\\missing"}""", "not_found")]
    [InlineData("portal", """{"folder":"Desktop"}""", "refused")]
    [InlineData("roll_up", """{"group":"Nope"}""", "not_found")]
    [InlineData("add_rule", """{"match":{"size":1},"group":"Work"}""", "bad_args")]
    [InlineData("add_rule", """{"match":{"ext":["p d f"]},"group":"Work"}""", "bad_args")]
    [InlineData("add_rule", """{"match":{"type":"car"},"group":"Work"}""", "bad_args")]
    [InlineData("new_group", """{"name":"projects"}""", "refused")]
    [InlineData("new_group", """{"name":"  "}""", "bad_args")]
    [InlineData("new_group", """{"name":"evil‮txt.exe"}""", "bad_args")]
    [InlineData("preset", """{"name":"Nope"}""", "not_found")]
    [InlineData("snapshot_restore", """{"name":"Nope"}""", "not_found")]
    [InlineData("snapshot_save", """{"name":"Before AI change 1"}""", "bad_args")]
    public void Bad_requests_get_the_right_code_and_change_nothing(string action, string args, string code)
    {
        using var d = new Desk();
        var before = d.Files();
        var answer = d.Ask("preview", action, JsonNode.Parse(args)!.AsObject());
        Assert.Equal(code, Desk.Error(answer));
        Assert.Equal(before, d.Files());
    }

    [Fact]
    public void A_missing_name_lists_the_valid_ones_so_a_model_can_correct_itself()
    {
        using var d = new Desk();
        Assert.Contains("\"Projects\", \"Work\"", Desk.Message(d.Ask("preview", "roll_up", new JsonObject { ["group"] = "Projekts" })));
        Assert.Contains("\"Gaming\"", Desk.Message(d.Ask("preview", "preset", new JsonObject { ["name"] = "Gamin" })));
    }

    [Fact]
    public void A_damaged_layout_is_refused_and_left_as_it_is()
    {
        using var d = new Desk();
        File.WriteAllText(d.Store.LayoutPath, "{ not json");
        Assert.Equal("failed", Desk.Error(d.Ask("preview", "new_group", new JsonObject { ["name"] = "X" })));
        Assert.Equal("failed", Desk.Error(d.Ask("list", "groups")));
        Assert.Equal("{ not json", File.ReadAllText(d.Store.LayoutPath));
        Assert.False(File.Exists(d.Store.LayoutBackupPath));
    }

    [Fact]
    public void Quick_hide_needs_the_running_app_and_signals_it()
    {
        using var d = new Desk();
        Assert.Equal("refused", Desk.Error(d.Ask("preview", "quick_hide")));
        var app = d.Signals.Create();                                            // stands in for the running Orgodesk
        try
        {
            var before = File.ReadAllText(d.Store.LayoutPath);
            var (_, apply) = d.Do("quick_hide");
            Assert.True(apply["ok"]!.GetValue<bool>(), apply.ToJsonString());
            Assert.True(app[1].WaitOne(0));                                      // hide
            Assert.False(app[2].WaitOne(0));
            Assert.Equal("preview(quick_hide, show=true)", apply["result"]!["tool"]!["undo"]!.GetValue<string>());
            Assert.Equal(before, File.ReadAllText(d.Store.LayoutPath));
            Assert.True(d.Do("quick_hide", new JsonObject { ["show"] = true }).Apply["ok"]!.GetValue<bool>());
            Assert.True(app[2].WaitOne(0));
            Assert.True(d.Ask("status")["result"]!["tool"]!["running"]!.GetValue<bool>());
        }
        finally { foreach (var e in app) e.Dispose(); }
    }

    [Fact]
    public void A_layout_change_tells_the_running_app_to_reload()
    {
        using var d = new Desk();
        var app = d.Signals.Create();
        try
        {
            var (_, apply) = d.Do("new_group", new JsonObject { ["name"] = "School" });
            Assert.True(apply["result"]!["tool"]!["shownNow"]!.GetValue<bool>());
            Assert.True(app[0].WaitOne(0));
        }
        finally { foreach (var e in app) e.Dispose(); }
    }

    [Fact]
    public void The_same_preview_cannot_be_applied_twice()
    {
        using var d = new Desk();
        var (p, _) = d.Do("new_group", new JsonObject { ["name"] = "School" });
        Assert.Equal("already_applied", Desk.Error(d.Ask("apply", previewId: Desk.Id(p))));
        Assert.Single(d.Layout.AllFences, f => f.Title == "School");
    }

    [Fact]
    public void The_library_undo_finds_nothing_of_orgodesks_and_touches_nothing()
    {
        using var d = new Desk();
        d.Do("new_group", new JsonObject { ["name"] = "School" });
        string layout = File.ReadAllText(d.Store.LayoutPath);
        Assert.Equal("not_found", Desk.Error(d.Ask("preview", "undo", new JsonObject { ["run"] = "last" })));
        Assert.Equal(layout, File.ReadAllText(d.Store.LayoutPath));
    }
}
