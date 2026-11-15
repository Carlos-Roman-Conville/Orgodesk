using System.Drawing;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Rules;
using Xunit;

namespace Orgodesk.Core.Tests;

/// <summary>Gap tests for LayoutReconciler: renamed and duplicate items, rule targets, full screens, rescale limits.</summary>
public class ReconcilerGapTests
{
    private static readonly LayoutMetrics M = new(80, 90, 48, 24, 4);
    private static readonly Rectangle Area = new(0, 0, 800, 600);
    private static ItemFacts F(string name, string? ext = null) => new("C:\\d\\" + name, name, ext, false, false, false);

    [Fact]
    public void Reconcile_KeyThatDiffersOnlyInCase_IsTheSameItem()
    {
        var layout = new Layout();
        var fence = new Fence { Id = "f", Bounds = new Rectangle(400, 0, 300, 300) };
        fence.Items.Add("C:\\D\\Report.TXT");
        layout.ActivePage.Fences.Add(fence);

        var r = LayoutReconciler.Reconcile(layout, new[] { F("report.txt", ".txt") }, Area, M);

        Assert.False(r.Changed);
        Assert.Equal(new[] { "C:\\D\\Report.TXT" }, fence.Items);
        Assert.Empty(layout.ActivePage.FreeItems);
    }

    [Fact]
    public void Reconcile_RenamedItem_IsRemovedUnderTheOldNameAndAddedUnderTheNew()
    {
        var layout = new Layout();
        var fence = new Fence { Id = "f", Bounds = new Rectangle(400, 0, 300, 300) };
        fence.Items.Add("C:\\d\\old.txt");
        layout.ActivePage.Fences.Add(fence);

        var r = LayoutReconciler.Reconcile(layout, new[] { F("new.txt", ".txt") }, Area, M);

        Assert.Equal(new[] { "C:\\d\\old.txt" }, r.Removed);
        Assert.Equal(new[] { "C:\\d\\new.txt" }, r.Added);
        Assert.Empty(fence.Items);
        Assert.True(layout.ActivePage.FreeItems.ContainsKey("C:\\d\\new.txt"));
    }

    [Fact]
    public void Reconcile_ItemInTwoFences_IsReportedRemovedOnce_AndLeavesBoth()
    {
        var layout = new Layout();
        var a = new Fence { Id = "a" }; a.Items.Add("C:\\d\\gone");
        var b = new Fence { Id = "b" }; b.Items.Add("C:\\d\\gone");
        layout.ActivePage.Fences.AddRange(new[] { a, b });

        var r = LayoutReconciler.Reconcile(layout, Array.Empty<ItemFacts>(), Area, M);

        Assert.Single(r.Removed);
        Assert.Empty(a.Items);
        Assert.Empty(b.Items);
    }

    [Fact]
    public void Reconcile_RemovesMissingItemsOnEveryPage_ButKeepsPresentOnes()
    {
        var layout = new Layout();
        var second = new Page();
        layout.Pages.Add(second);
        layout.Pages[0].FreeItems["C:\\d\\keep"] = new Point(0, 0);
        second.FreeItems["C:\\d\\gone"] = new Point(0, 0);
        var fence = new Fence { Id = "f" };
        fence.Items.AddRange(new[] { "C:\\d\\keep2", "C:\\d\\gone2" });
        second.Fences.Add(fence);

        LayoutReconciler.Reconcile(layout, new[] { F("keep"), F("keep2") }, Area, M);

        Assert.Single(layout.Pages[0].FreeItems);
        Assert.Empty(second.FreeItems);
        Assert.Equal(new[] { "C:\\d\\keep2" }, fence.Items);
    }

    [Fact]
    public void Reconcile_SameItemListedTwiceByTheDesktop_IsPlacedOnce()
    {
        var layout = new Layout();
        var r = LayoutReconciler.Reconcile(layout, new[] { F("a"), F("a") }, Area, M);
        Assert.Single(r.Added);
        Assert.Single(layout.ActivePage.FreeItems);
    }

    [Fact]
    public void Reconcile_NewItemsGoToTheActivePage_NotTheFirst()
    {
        var layout = new Layout();
        layout.Pages.Add(new Page());
        layout.ActivePageIndex = 1;
        LayoutReconciler.Reconcile(layout, new[] { F("a") }, Area, M);
        Assert.Empty(layout.Pages[0].FreeItems);
        Assert.Single(layout.Pages[1].FreeItems);
    }

    [Fact]
    public void Reconcile_RuleTargetsAPortalFence_ItemFallsBackToTheFreeDesktop()
    {
        var layout = new Layout();
        layout.ActivePage.Fences.Add(new Fence { Id = "portal", PortalPath = "C:\\x", Bounds = new Rectangle(400, 0, 200, 200) });
        layout.Rules.Add(new Rule { Kind = RuleKind.Extension, Pattern = "txt", TargetFenceId = "portal" });
        LayoutReconciler.Reconcile(layout, new[] { F("a.txt", ".txt") }, Area, M);
        Assert.Empty(layout.ActivePage.FindFence("portal")!.Items);
        Assert.True(layout.ActivePage.FreeItems.ContainsKey("C:\\d\\a.txt"));
    }

    [Fact]
    public void Reconcile_RuleTargetsADeletedFence_ItemFallsBackToTheFreeDesktop()
    {
        var layout = new Layout();
        layout.Rules.Add(new Rule { Kind = RuleKind.Extension, Pattern = "txt", TargetFenceId = "no-such-fence" });
        LayoutReconciler.Reconcile(layout, new[] { F("a.txt", ".txt") }, Area, M);
        Assert.True(layout.ActivePage.FreeItems.ContainsKey("C:\\d\\a.txt"));
    }

    [Fact]
    public void Reconcile_MoreItemsThanFreeCells_PlacesEveryItemWithoutThrowing()
    {
        var layout = new Layout();
        var tiny = new Rectangle(0, 0, 160, 180);      // 2 x 2 cells
        var items = Enumerable.Range(0, 10).Select(i => F("i" + i)).ToArray();
        var r = LayoutReconciler.Reconcile(layout, items, tiny, M);
        Assert.Equal(10, r.Added.Count);
        Assert.Equal(10, layout.ActivePage.FreeItems.Count);
    }

    [Fact]
    public void Reconcile_ASecondRunWithNothingNew_ChangesNothing()
    {
        var layout = new Layout();
        var items = new[] { F("a"), F("b"), F("c.png", ".png") };
        LayoutReconciler.Reconcile(layout, items, Area, M);
        var before = layout.ActivePage.FreeItems.ToDictionary(kv => kv.Key, kv => kv.Value);
        var r = LayoutReconciler.Reconcile(layout, items, Area, M);
        Assert.False(r.Changed);
        Assert.Equal(before, layout.ActivePage.FreeItems.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    [Fact]
    public void ApplyRules_ItemAlreadyInItsTargetFence_IsNotCountedOrDuplicated()
    {
        var layout = new Layout();
        var docs = new Fence { Id = "docs" };
        docs.Items.Add("C:\\d\\A.TXT");
        layout.ActivePage.Fences.Add(docs);
        layout.Rules.Add(new Rule { Kind = RuleKind.Extension, Pattern = "txt", TargetFenceId = "docs" });
        int moved = LayoutReconciler.ApplyRules(layout, new[] { F("a.txt", ".txt") });
        Assert.Equal(0, moved);
        Assert.Single(docs.Items);
    }

    [Fact]
    public void ApplyRules_ItemOnAnotherPage_MovesAndLeavesNoCopyBehind()
    {
        var layout = new Layout();
        var second = new Page();
        second.FreeItems["C:\\d\\a.txt"] = new Point(0, 0);
        layout.Pages.Add(second);
        var docs = new Fence { Id = "docs" };
        layout.Pages[0].Fences.Add(docs);
        layout.Rules.Add(new Rule { Kind = RuleKind.Extension, Pattern = "txt", TargetFenceId = "docs" });

        LayoutReconciler.ApplyRules(layout, new[] { F("a.txt", ".txt") });

        Assert.Empty(second.FreeItems);
        Assert.Equal(new[] { "C:\\d\\a.txt" }, docs.Items);
    }

    [Fact]
    public void ApplyRules_PortalTargetOrMissingFence_MovesNothing()
    {
        var layout = new Layout();
        layout.ActivePage.Fences.Add(new Fence { Id = "portal", PortalPath = "C:\\x" });
        layout.ActivePage.FreeItems["C:\\d\\a.txt"] = new Point(0, 0);
        layout.Rules.Add(new Rule { Kind = RuleKind.Extension, Pattern = "txt", TargetFenceId = "portal" });
        layout.Rules.Add(new Rule { Kind = RuleKind.Extension, Pattern = "txt", TargetFenceId = "gone" });
        Assert.Equal(0, LayoutReconciler.ApplyRules(layout, new[] { F("a.txt", ".txt") }));
        Assert.True(layout.ActivePage.FreeItems.ContainsKey("C:\\d\\a.txt"));
    }

    [Fact]
    public void ReleaseFence_EmptyFence_JustDisappears()
    {
        var layout = new Layout();
        var f = new Fence { Id = "f" };
        layout.ActivePage.Fences.Add(f);
        LayoutReconciler.ReleaseFence(layout.ActivePage, f, Area, M);
        Assert.Empty(layout.ActivePage.Fences);
        Assert.Empty(layout.ActivePage.FreeItems);
    }

    [Fact]
    public void ReleaseFence_ItemsLandOnDifferentCells()
    {
        var layout = new Layout();
        var f = new Fence { Id = "f", Bounds = new Rectangle(0, 0, 80, 270) };
        f.Items.AddRange(new[] { "a", "b", "c" });
        layout.ActivePage.Fences.Add(f);
        LayoutReconciler.ReleaseFence(layout.ActivePage, f, Area, M);
        Assert.Equal(3, layout.ActivePage.FreeItems.Values.Distinct().Count());
    }

    [Fact]
    public void ClampToArea_NothingOutside_ChangesNothing()
    {
        var layout = new Layout();
        layout.ActivePage.FreeItems["a"] = new Point(80, 90);
        layout.ActivePage.Fences.Add(new Fence { Bounds = new Rectangle(400, 100, 300, 200) });
        Assert.Equal(0, LayoutReconciler.ClampToArea(layout, Area, M));
        Assert.Equal(new Point(80, 90), layout.ActivePage.FreeItems["a"]);
    }

    [Fact]
    public void ClampToArea_ScreenShrinks_NoIconIsLost()
    {
        var layout = new Layout();
        for (int i = 0; i < 6; i++) layout.ActivePage.FreeItems["k" + i] = new Point(i * 80, 450);
        LayoutReconciler.ClampToArea(layout, new Rectangle(0, 0, 240, 180), M);
        Assert.Equal(6, layout.ActivePage.FreeItems.Count);
    }

    [Fact]
    public void SpreadOverlaps_NoRoomLeft_IconsStayWhereTheyWere()
    {
        var page = new Page();
        page.FreeItems["a"] = new Point(0, 0);
        page.FreeItems["b"] = new Point(0, 0);
        var one = new Rectangle(0, 0, 80, 90);       // a single cell
        int moved = LayoutReconciler.SpreadOverlaps(page, one, M);
        Assert.Equal(0, moved);
        Assert.Equal(2, page.FreeItems.Count);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.5)]
    public void Rescale_ZeroOrNegativeScale_IsIgnored(double scale)
    {
        var layout = new Layout { GridScale = 1.0 };
        layout.ActivePage.FreeItems["a"] = new Point(160, 180);
        Assert.False(LayoutReconciler.Rescale(layout, scale, Area, M));
        Assert.Equal(1.0, layout.GridScale);
        Assert.Equal(new Point(160, 180), layout.ActivePage.FreeItems["a"]);
    }

    [Fact]
    public void Rescale_WithinOnePercent_ChangesNothing()
    {
        var layout = new Layout { GridScale = 1.0 };
        layout.ActivePage.FreeItems["a"] = new Point(160, 180);
        Assert.False(LayoutReconciler.Rescale(layout, 1.005, Area, M));
        Assert.Equal(new Point(160, 180), layout.ActivePage.FreeItems["a"]);
    }

    [Fact]
    public void Rescale_UpThenBack_KeepsEveryIcon()
    {
        var layout = new Layout { GridScale = 1.0 };
        for (int i = 0; i < 8; i++) layout.ActivePage.FreeItems["k" + i] = new Point(i % 4 * 80, i / 4 * 90);
        LayoutReconciler.Rescale(layout, 1.5, new Rectangle(0, 0, 1200, 900), new LayoutMetrics(120, 135, 72, 36, 6));
        LayoutReconciler.Rescale(layout, 1.0, Area, M);
        Assert.Equal(8, layout.ActivePage.FreeItems.Count);
        Assert.Equal(1.0, layout.GridScale);
        Assert.Equal(8, layout.ActivePage.FreeItems.Values.Distinct().Count());
    }
}
