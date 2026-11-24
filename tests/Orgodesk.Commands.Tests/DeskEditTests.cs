using System.Text.Json.Nodes;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Rules;

namespace Orgodesk.Commands.Tests;

/// <summary>FC-57: rename_group, remove_group and move_icon, each with a preview, apply and the snapshot undo.</summary>
public class DeskEditTests
{
    private static string Says(JsonNode preview) =>
        string.Join("\n", preview["result"]!["items"]!.AsArray().Select(i => i!["text"]!.GetValue<string>()));

    public static IEnumerable<object[]> Previews() => new[]
    {
        new object[] { "rename_group", """{"group":"Projects","name":"Work 2"}""" },
        new object[] { "remove_group", """{"group":"Projects"}""" },
        new object[] { "move_icon", """{"icon":"a.pdf","to":"Projects"}""" },
    };

    [Theory]
    [MemberData(nameof(Previews))]
    public void A_preview_changes_nothing(string action, string args)
    {
        using var d = new Desk();
        var before = d.Files();
        Assert.True(d.Ask("preview", action, JsonNode.Parse(args)!.AsObject())["ok"]!.GetValue<bool>());
        Assert.Equal(before, d.Files());
    }

    [Fact]
    public void Rename_move_and_remove_apply_and_the_first_before_snapshot_puts_the_whole_layout_back()
    {
        using var d = new Desk(l => l.Rules.Add(new Rule { Name = "Docs", Kind = RuleKind.Extension, Pattern = "docx", TargetFenceId = "projects" }));
        string original = File.ReadAllText(d.Store.LayoutPath);

        var (p1, a1) = d.Do("rename_group", new JsonObject { ["group"] = "projects", ["name"] = "Docs" });
        Assert.Contains("Renames the group \"Projects\" to \"Docs\"", Says(p1));
        Assert.Equal("Docs", d.Layout.FindFence("projects")!.Title);
        Assert.Equal(new[] { d.Key("plan.docx") }, d.Layout.FindFence("projects")!.Items);
        Assert.Equal("refused", Desk.Error(d.Ask("preview", "rename_group", new JsonObject { ["group"] = "Docs", ["name"] = "work" })));
        Assert.Equal("refused", Desk.Error(d.Ask("preview", "rename_group", new JsonObject { ["group"] = "Docs", ["name"] = "Docs" })));
        Assert.Equal("bad_args", Desk.Error(d.Ask("preview", "rename_group", new JsonObject { ["group"] = "Docs", ["name"] = "Desktop" })));
        Assert.Equal("bad_args", Desk.Error(d.Ask("preview", "new_group", new JsonObject { ["name"] = "desktop" })));

        var (p2, _) = d.Do("move_icon", new JsonObject { ["icon"] = "a", ["to"] = "work" });        // without its ending
        Assert.Contains("Moves \"a.pdf\" from the free desktop into the group \"Work\"", Says(p2));
        Assert.Equal(new[] { d.Key("a.pdf") }, d.Layout.FindFence("work")!.Items);
        Assert.False(d.Layout.ActivePage.FreeItems.ContainsKey(d.Key("a.pdf")));
        Assert.True(d.Do("move_icon", new JsonObject { ["icon"] = "a.pdf", ["to"] = "Desktop" }).Apply["ok"]!.GetValue<bool>());
        Assert.Empty(d.Layout.FindFence("work")!.Items);
        Assert.True(d.Layout.ActivePage.FreeItems.ContainsKey(d.Key("a.pdf")));

        var (p3, _) = d.Do("remove_group", new JsonObject { ["group"] = "Docs" });
        Assert.Contains("\"plan.docx\"", Says(p3));
        Assert.Contains("1 sort rule go with it", Says(p3));
        Assert.Contains("No file is deleted or moved", Says(p3));
        Assert.Null(d.Layout.FindFence("projects"));
        Assert.Empty(d.Layout.Rules);
        Assert.True(d.Layout.ActivePage.FreeItems.ContainsKey(d.Key("plan.docx")));

        string first = a1["result"]!["tool"]!["undo"]!.GetValue<string>().Split('"')[1];
        Assert.True(d.Do("snapshot_restore", new JsonObject { ["name"] = first }).Apply["ok"]!.GetValue<bool>());
        Assert.Equal(JsonNode.Parse(original)!.ToJsonString(), JsonNode.Parse(File.ReadAllText(d.Store.LayoutPath))!.ToJsonString());
    }

    [Theory]
    [InlineData("move_icon", """{"icon":"ghost.txt","to":"Work"}""", "not_found")]
    [InlineData("move_icon", """{"icon":"a.pdf","to":"Nowhere"}""", "not_found")]
    [InlineData("move_icon", """{"icon":"plan.docx","to":"Projects"}""", "refused")]        // already there
    [InlineData("move_icon", """{"icon":"a.pdf","to":"desktop"}""", "refused")]             // already free
    [InlineData("move_icon", """{"icon":"a.pdf\u202Etxt","to":"Work"}""", "bad_args")]
    [InlineData("remove_group", """{"group":"Nope"}""", "not_found")]
    [InlineData("rename_group", """{"group":"Nope","name":"X"}""", "not_found")]
    public void Bad_icon_and_group_requests_get_the_right_code_and_change_nothing(string action, string args, string code)
    {
        using var d = new Desk();
        var before = d.Files();
        Assert.Equal(code, Desk.Error(d.Ask("preview", action, JsonNode.Parse(args)!.AsObject())));
        Assert.Equal(before, d.Files());
    }

    [Fact]
    public void Two_icons_with_one_name_ask_for_the_whole_name()
    {
        using var d = new Desk(l => l.ActivePage.FreeItems[@"C:\Users\Public\Desktop\a.pdf"] = new System.Drawing.Point(0, 500));
        Assert.Equal("bad_args", Desk.Error(d.Ask("preview", "move_icon", new JsonObject { ["icon"] = "a.pdf", ["to"] = "Work" })));
    }

    [Fact]
    public void A_folder_group_takes_no_icons_and_can_be_removed_without_touching_the_folder()
    {
        using var d = new Desk(l => l.ActivePage.Fences.Add(new Fence { Id = "p", Title = "Pics", PortalPath = @"C:\Pictures", Bounds = new System.Drawing.Rectangle(100, 600, 300, 240) }));
        Assert.Equal("refused", Desk.Error(d.Ask("preview", "move_icon", new JsonObject { ["icon"] = "a.pdf", ["to"] = "Pics" })));
        var (p, apply) = d.Do("remove_group", new JsonObject { ["group"] = "Pics" });
        Assert.Contains(@"(it showed C:\Pictures)", Says(p));
        Assert.True(apply["ok"]!.GetValue<bool>());
        Assert.Null(d.Layout.FindFence("p"));
    }
}
