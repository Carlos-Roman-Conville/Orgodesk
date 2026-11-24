using System.Text.Json.Nodes;
using Orgodesk.Core.Fences;

namespace Orgodesk.Commands.Tests;

/// <summary>
/// FC-55, the shared lock (orgo-commands DESIGN.md rule 7, OC-2): the app and command mode never write layout.json at the
/// same time, a command never writes between the person's edit and its save, and the app never writes an older layout
/// over a command's change. Each test fails with its lock removed (checked by hand; see ISSUES.md FC-55).
/// </summary>
public class SharedLayoutTests
{
    /// <summary>Runs on a thread of its own, as another process would (Task.Run can run inline, on the thread holding the mutex).</summary>
    private static T OtherThread<T>(Func<T> f)
    {
        T result = default!;
        var t = new Thread(() => result = f());
        t.Start();
        t.Join();
        return result;
    }

    [Fact]
    public void The_apps_save_waits_while_a_command_holds_the_lock_and_writes_nothing()
    {
        using var d = new Desk();
        var app = new SharedLayout(d.Store, d.Lock);
        var layout = app.Load();
        layout.ActivePage.Fences.Add(new Fence { Title = "Mine" });
        string before = File.ReadAllText(d.Store.LayoutPath);
        using (new LockHolder(d.Lock))
        {
            Assert.Equal(SharedLayout.Outcome.Busy, app.Save(layout, out _));
            Assert.Equal(before, File.ReadAllText(d.Store.LayoutPath));
        }
        Assert.Equal(SharedLayout.Outcome.Saved, app.Save(layout, out _));
        Assert.Contains(d.Layout.AllFences, f => f.Title == "Mine");
    }

    [Fact]
    public void A_command_apply_waits_while_the_person_has_an_unsaved_edit_then_answers_busy_and_uses_nothing_up()
    {
        using var d = new Desk();
        var app = new SharedLayout(d.Store, d.Lock);
        var layout = app.Load();
        var p = d.Ask("preview", "new_group", new JsonObject { ["name"] = "School" });

        layout.ActivePage.Fences.Add(new Fence { Title = "Mine" });
        app.Hold();                                                              // the person's edit, not saved yet
        Assert.True(app.Holding);
        var busy = OtherThread(() => d.Ask("apply", previewId: Desk.Id(p)));  // command mode is another thread here, as another process
        Assert.Equal("busy", Desk.Error(busy));
        Assert.DoesNotContain(d.Layout.AllFences, f => f.Title == "School");

        Assert.Equal(SharedLayout.Outcome.Saved, app.Save(layout, out _));       // the save lets go of the hold
        var apply = OtherThread(() => d.Ask("apply", previewId: Desk.Id(p)));
        Assert.True(apply["ok"]!.GetValue<bool>(), apply.ToJsonString());       // the same preview applies afterwards
        Assert.Contains(d.Layout.AllFences, f => f.Title == "School");
        Assert.Contains(d.Layout.AllFences, f => f.Title == "Mine");              // and the person's edit is kept
    }

    [Fact]
    public void A_command_apply_while_another_writer_holds_the_lock_answers_busy_and_changes_nothing()
    {
        using var d = new Desk();
        var p = d.Ask("preview", "roll_up", new JsonObject { ["group"] = "Projects" });
        string before = File.ReadAllText(d.Store.LayoutPath);
        using (new LockHolder(d.Lock))
        {
            Assert.Equal("busy", Desk.Error(d.Ask("apply", previewId: Desk.Id(p))));
            Assert.Equal(before, File.ReadAllText(d.Store.LayoutPath));
            Assert.Empty(d.Store.ListSnapshots());
        }
        Assert.True(d.Ask("apply", previewId: Desk.Id(p))["ok"]!.GetValue<bool>());
    }

    [Fact]
    public void The_change_itself_is_made_under_the_lock_even_when_it_was_taken_after_the_first_check()
    {
        using var d = new Desk();
        var args = new JsonObject { ["group"] = "Projects" };
        d.Ask("preview", "roll_up", args.DeepClone().AsObject());                 // shown, so only the lock can stop it
        string before = File.ReadAllText(d.Store.LayoutPath);
        using (new LockHolder(d.Lock))
        {
            var ex = Assert.Throws<Orgo.Commands.CommandException>(() => d.Tool.ApplyOther("roll_up", args));
            Assert.Equal("busy", ex.Code);
            Assert.Equal(before, File.ReadAllText(d.Store.LayoutPath));
        }
    }

    [Fact]
    public void The_app_never_writes_an_older_layout_over_a_commands_change()
    {
        using var d = new Desk();
        var app = new SharedLayout(d.Store, d.Lock);
        var stale = app.Load();
        Assert.True(d.Do("new_group", new JsonObject { ["name"] = "School" }).Apply["ok"]!.GetValue<bool>());

        stale.ActivePage.Fences.RemoveAll(f => f.Title == "Projects");          // an edit made on the old copy
        Assert.Equal(SharedLayout.Outcome.TookDisk, app.Save(stale, out var disk));
        Assert.Contains(disk!.AllFences, f => f.Title == "School");
        Assert.Contains(d.Layout.AllFences, f => f.Title == "School");
        Assert.Contains(d.Layout.AllFences, f => f.Title == "Projects");
        Assert.Equal(SharedLayout.Outcome.Saved, app.Save(disk, out _));         // from then on it saves normally
    }

    [Fact]
    public void After_a_commands_signal_the_app_reads_the_new_layout_once()
    {
        using var d = new Desk();
        var app = new SharedLayout(d.Store, d.Lock);
        app.Load();
        Assert.Null(app.Changed());
        d.Do("new_group", new JsonObject { ["name"] = "School" });
        Assert.Contains(app.Changed()!.AllFences, f => f.Title == "School");
        Assert.Null(app.Changed());
    }

    [Fact]
    public void The_apps_snapshot_writes_wait_for_the_lock()
    {
        using var d = new Desk();
        var app = new SharedLayout(d.Store, d.Lock);
        var layout = app.Load();
        using (new LockHolder(d.Lock))
            Assert.False(app.Run(() => d.Store.SaveSnapshot("Mine", layout), TimeSpan.FromMilliseconds(100)));
        Assert.Empty(d.Store.ListSnapshots());
        Assert.True(app.Run(() => d.Store.SaveSnapshot("Mine", layout), TimeSpan.FromMilliseconds(100)));
        Assert.Single(d.Store.ListSnapshots());
    }
}
