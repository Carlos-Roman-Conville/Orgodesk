using System.Drawing;
using Orgodesk.Core.Fences;
using Xunit;

namespace Orgodesk.Core.Tests;

/// <summary>FC-32 and FC-33: icons must not overlap after a display-scale or resolution change.</summary>
public class GridScaleTests
{
    private static readonly Rectangle Area = new(0, 0, 1600, 900);

    private static bool Overlaps(Page page, LayoutMetrics m)
    {
        var cells = page.FreeItems.Values.Select(p => FreeGrid.CellAt(p, m)).ToList();
        for (int i = 0; i < cells.Count; i++)
            for (int j = i + 1; j < cells.Count; j++)
                if (cells[i].IntersectsWith(cells[j])) return true;
        return false;
    }

    private static Layout ColumnOfIcons(LayoutMetrics m, int count)
    {
        var l = new Layout { GridScale = 1.0 };
        for (int i = 0; i < count; i++) l.ActivePage.FreeItems[$"item{i}"] = new Point(0, i * m.CellHeight);
        for (int i = 0; i < count; i++) l.ActivePage.FreeItems[$"col2-{i}"] = new Point(m.CellWidth, i * m.CellHeight);
        return l;
    }

    [Fact]
    public void Rescale_To150_NoOverlaps_AndScaleRecorded()
    {
        var m100 = LayoutMetrics.Default;
        var l = ColumnOfIcons(m100, 8);
        l.ActivePage.Fences.Add(new Fence { Id = "f", Title = "Places", Bounds = new Rectangle(400, 120, 300, 200) });
        var m150 = m100.Scale(1.5);

        Assert.True(LayoutReconciler.Rescale(l, 1.5, Area, m150));
        Assert.Equal(1.5, l.GridScale);
        Assert.False(Overlaps(l.ActivePage, m150));
        Assert.Equal(16, l.ActivePage.FreeItems.Count);
        Assert.Equal(new Rectangle(600, 180, 450, 300), l.ActivePage.Fences[0].Bounds);
        Assert.False(LayoutReconciler.Rescale(l, 1.5, Area, m150));   // nothing to do the second time
    }

    [Fact]
    public void Rescale_UnknownScale_OnlyRecordsIt()
    {
        var m = LayoutMetrics.Default;
        var l = ColumnOfIcons(m, 3);
        l.GridScale = 0;
        var before = l.ActivePage.FreeItems.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.True(LayoutReconciler.Rescale(l, 1.25, Area, m.Scale(1.25)));
        Assert.Equal(1.25, l.GridScale);
        Assert.Equal(before, l.ActivePage.FreeItems.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    [Fact]
    public void Rescale_AlsoConvertsPresets()
    {
        var m = LayoutMetrics.Default;
        var l = ColumnOfIcons(m, 4);
        var preset = l.ActivePage.Clone();
        l.Presets.Add(preset);
        LayoutReconciler.Rescale(l, 1.5, Area, m.Scale(1.5));
        Assert.False(Overlaps(l.Presets[0], m.Scale(1.5)));
    }

    [Fact]
    public void ClampToArea_PulledBackIcon_NeverLandsOnAnother()
    {
        // FC-33: screen shrinks from 768 to 600 high; an icon below the new edge must find a free cell.
        var m = LayoutMetrics.Default;
        var l = new Layout();
        for (int i = 0; i < 8; i++) l.ActivePage.FreeItems[$"i{i}"] = new Point(0, i * m.CellHeight);
        LayoutReconciler.ClampToArea(l, new Rectangle(0, 0, 800, 600), m);
        Assert.False(Overlaps(l.ActivePage, m));
        Assert.All(l.ActivePage.FreeItems.Values, p => Assert.True(new Rectangle(0, 0, 800, 600).Contains(FreeGrid.CellAt(p, m))));
    }

    [Fact]
    public void ClampToArea_SpreadsIconsStackedWhileTheScreenWasFull()
    {
        var m = LayoutMetrics.Default;
        var l = new Layout();
        l.ActivePage.FreeItems["a"] = new Point(0, 0);
        l.ActivePage.FreeItems["b"] = new Point(0, 0);   // stacked when there was no free cell
        l.ActivePage.FreeItems["c"] = new Point(0, m.CellHeight);
        LayoutReconciler.ClampToArea(l, Area, m);
        Assert.False(Overlaps(l.ActivePage, m));
        Assert.Equal(new Point(0, 0), l.ActivePage.FreeItems["a"]);
    }
}
