using Orgodesk.Core.Rules;
using Xunit;

namespace Orgodesk.Core.Tests;

public class RulesEngineTests
{
    private static ItemFacts File(string name, string ext) => new("C:\\d\\" + name, name, ext, false, false, false);
    private static ItemFacts Folder(string name) => new("C:\\d\\" + name, name, null, true, false, false);
    private static ItemFacts Link(string name) => new("C:\\d\\" + name + ".lnk", name, ".lnk", false, true, false);
    private static readonly ItemFacts RecycleBin = new("::{645FF040-5081-101B-9F08-00AA002F954E}", "Recycle Bin", null, true, false, true);

    [Fact]
    public void Extension_MatchesAnyListedExtension()
    {
        var r = new Rule { Kind = RuleKind.Extension, Pattern = "jpg; PNG,gif", TargetFenceId = "f1" };
        Assert.True(RulesEngine.Matches(r, File("a.png", ".png")));
        Assert.True(RulesEngine.Matches(r, File("a.jpg", "jpg")));
        Assert.False(RulesEngine.Matches(r, File("a.txt", ".txt")));
        Assert.False(RulesEngine.Matches(r, Folder("png")));
    }

    [Fact]
    public void NamePattern_UsesGlob()
    {
        var r = new Rule { Kind = RuleKind.NamePattern, Pattern = "Report*", TargetFenceId = "f1" };
        Assert.True(RulesEngine.Matches(r, File("report 2026.xlsx", ".xlsx")));
        Assert.False(RulesEngine.Matches(r, File("My Report.xlsx", ".xlsx")));
        var q = new Rule { Kind = RuleKind.NamePattern, Pattern = "v?.txt", TargetFenceId = "f1" };
        Assert.True(RulesEngine.Matches(q, File("v1.txt", ".txt")));
        Assert.False(RulesEngine.Matches(q, File("v10.txt", ".txt")));
    }

    [Fact]
    public void ItemType_FiltersEveryKind()
    {
        var folders = new Rule { Kind = RuleKind.ItemType, ItemType = ItemType.Folder, TargetFenceId = "f" };
        var links = new Rule { Kind = RuleKind.ItemType, ItemType = ItemType.Shortcut, TargetFenceId = "f" };
        var sys = new Rule { Kind = RuleKind.ItemType, ItemType = ItemType.System, TargetFenceId = "f" };
        Assert.True(RulesEngine.Matches(folders, Folder("x")));
        Assert.False(RulesEngine.Matches(folders, RecycleBin)); // system items are not plain folders
        Assert.True(RulesEngine.Matches(links, Link("Chrome")));
        Assert.True(RulesEngine.Matches(sys, RecycleBin));
        Assert.False(RulesEngine.Matches(sys, File("a.txt", ".txt")));
    }

    [Fact]
    public void ItemType_AnyOnItsOwnMatchesNothing()
    {
        var r = new Rule { Kind = RuleKind.ItemType, ItemType = ItemType.Any, TargetFenceId = "f" };
        Assert.False(RulesEngine.Matches(r, File("a.txt", ".txt")));
    }

    [Fact]
    public void Match_FirstEnabledRuleWins_DisabledAndTargetlessSkipped()
    {
        var rules = new[]
        {
            new Rule { Kind = RuleKind.Extension, Pattern = "txt", TargetFenceId = "disabled", Enabled = false },
            new Rule { Kind = RuleKind.Extension, Pattern = "txt", TargetFenceId = "" },
            new Rule { Kind = RuleKind.Extension, Pattern = "txt", TargetFenceId = "docs" },
            new Rule { Kind = RuleKind.Extension, Pattern = "txt", TargetFenceId = "later" },
        };
        Assert.Equal("docs", RulesEngine.Match(rules, File("a.txt", ".txt")));
        Assert.Null(RulesEngine.Match(rules, File("a.exe", ".exe")));
    }
}
