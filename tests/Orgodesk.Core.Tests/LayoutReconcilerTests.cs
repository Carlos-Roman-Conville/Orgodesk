using System.Drawing;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Rules;
using Xunit;

namespace Orgodesk.Core.Tests;

public class LayoutReconcilerTests
{
    private static readonly LayoutMetrics M = new(80, 90, 48, 24, 4);
    private static readonly Rectangle Area = new(0, 0, 800, 600);
    private static ItemFacts File(string name, string ext) => new("C:\\d\\" + name, name, ext, false, false, false);

    [Fact]
    public void Reconcile_RemovesMissingAndPlacesNew()
    {
        var layout = new Layout();
        var fence = new Fence { Id = "docs", Bounds = new Rectangle(400, 0, 300, 300) };
        layout.ActivePage.Fences.Add(fence);
        fence.Items.Add("C:\\d\\old.txt");
        layout.ActivePage.FreeItems["C:\\d\\gone.png"] = new Point(0, 0);

        var current = new[] { File("new.txt", ".txt") };
        var result = LayoutReconciler.Reconcile(layout, current, Area, M);

        Assert.Contains("C:\\d\\old.txt", result.Removed);
        Assert.Contains("C:\\d\\gone.png", result.Removed);
        Assert.Single(result.Added);
        Assert.Empty(fence.Items);
        Assert.True(layout.ActivePage.FreeItems.ContainsKey("C:\\d\\new.txt"));
    }

    [Fact]
    public void Reconcile_NewItemGoesToRuleTarget()
    {
        var layout = new Layout();
        var fence = new Fence { Id = "images", Bounds = new Rectangle(400, 0, 300, 300) };
        layout.ActivePage.Fences.Add(fence);
        layout.Rules.Add(new Rule { Kind = RuleKind.Extension, Pattern = "png", TargetFenceId = "images" });

        LayoutReconciler.Reconcile(layout, new[] { File("pic.png", ".png") }, Area, M);

        Assert.Contains("C:\\d\\pic.png", fence.Items);
        Assert.Empty(layout.ActivePage.FreeItems);
    }

    [Fact]
    public void Reconcile_FreeSlotsAvoidFencesAndEachOther()
    {
        var layout = new Layout();
        // A fence covering the first column entirely.
        layout.ActivePage.Fences.Add(new Fence { Id = "f", Bounds = new Rectangle(0, 0, 80, 600) });
        LayoutReconciler.Reconcile(layout, new[] { File("a", null!), File("b", null!) }, Area, M);
        var a = layout.ActivePage.FreeItems["C:\\d\\a"];
        var b = layout.ActivePage.FreeItems["C:\\d\\b"];
        Assert.Equal(new Point(80, 0), a);
        Assert.Equal(new Point(80, 90), b);
    }

    [Fact]
    public void Reconcile_KeepsExistingPlacement()
    {
        var layout = new Layout();
        layout.ActivePage.FreeItems["C:\\d\\a"] = new Point(160, 180);
        var r = LayoutReconciler.Reconcile(layout, new[] { File("a", null!) }, Area, M);
        Assert.False(r.Changed);
        Assert.Equal(new Point(160, 180), layout.ActivePage.FreeItems["C:\\d\\a"]);
    }

    [Fact]
    public void ApplyRules_MovesItemsOutOfOtherPlaces()
    {
        var layout = new Layout();
        var docs = new Fence { Id = "docs" };
        var misc = new Fence { Id = "misc" };
        layout.ActivePage.Fences.AddRange(new[] { docs, misc });
        misc.Items.Add("C:\\d\\a.txt");
        layout.ActivePage.FreeItems["C:\\d\\b.txt"] = new Point(0, 0);
        layout.Rules.Add(new Rule { Kind = RuleKind.Extension, Pattern = "txt", TargetFenceId = "docs" });

        int moved = LayoutReconciler.ApplyRules(layout, new[] { File("a.txt", ".txt"), File("b.txt", ".txt") });

        Assert.Equal(2, moved);
        Assert.Equal(new[] { "C:\\d\\a.txt", "C:\\d\\b.txt" }, docs.Items);
        Assert.Empty(misc.Items);
        Assert.Empty(layout.ActivePage.FreeItems);
    }

    [Fact]
    public void ReleaseFence_ReturnsItemsToDesktop()
    {
        var layout = new Layout();
        var f = new Fence { Id = "f", Bounds = new Rectangle(300, 300, 200, 200) };
        f.Items.AddRange(new[] { "k1", "k2" });
        layout.ActivePage.Fences.Add(f);
        LayoutReconciler.ReleaseFence(layout.ActivePage, f, Area, M);
        Assert.Empty(layout.ActivePage.Fences);
        Assert.Equal(2, layout.ActivePage.FreeItems.Count);
    }

    [Fact]
    public void EvictFreeIconsUnderFences_MovesCoveredIconsOnly()
    {
        var layout = new Layout();
        var page = layout.ActivePage;
        page.Fences.Add(new Fence { Bounds = new Rectangle(0, 0, 200, 200) });
        page.Fences.Add(new Fence { Bounds = new Rectangle(400, 400, 200, 200), RolledUp = true });
        page.FreeItems["under"] = new Point(80, 90);       // inside the first fence
        page.FreeItems["clear"] = new Point(240, 0);       // outside both
        page.FreeItems["underRolledBody"] = new Point(400, 500); // under the rolled fence body, which is not visible
        page.FreeItems["underRolledTitle"] = new Point(400, 400); // under the rolled title bar

        int moved = LayoutReconciler.EvictFreeIconsUnderFences(page, Area, M);

        Assert.Equal(2, moved);
        Assert.Equal(new Point(240, 0), page.FreeItems["clear"]);
        Assert.Equal(new Point(400, 500), page.FreeItems["underRolledBody"]);
        foreach (var (key, pos) in page.FreeItems)
            Assert.False(page.Fences.Any(f => FenceLayoutEngine.VisibleBounds(f, M).IntersectsWith(FreeGrid.CellAt(pos, M))), key);
    }

    [Fact]
    public void ClampToArea_PullsOffscreenThingsBack()
    {
        var layout = new Layout();
        layout.ActivePage.Fences.Add(new Fence { Bounds = new Rectangle(2000, 2000, 300, 200) });
        layout.ActivePage.FreeItems["k"] = new Point(5000, 10);
        int changed = LayoutReconciler.ClampToArea(layout, Area, M);
        Assert.Equal(2, changed);
        Assert.True(Area.Contains(layout.ActivePage.Fences[0].Bounds));
        Assert.True(Area.Contains(FreeGrid.CellAt(layout.ActivePage.FreeItems["k"], M)));
    }
}
