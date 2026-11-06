using System.Drawing;
using Orgodesk.Core.Fences;
using Xunit;

namespace Orgodesk.Core.Tests;

public class FenceSnapperTests
{
    private static readonly Rectangle Area = new(0, 0, 1000, 800);

    [Fact]
    public void Move_SnapsToScreenEdge()
    {
        var r = FenceSnapper.Snap(new Rectangle(7, 300, 200, 100), Array.Empty<Rectangle>(), Area, 12, 6, FenceSnapper.Edges.All);
        Assert.Equal(new Rectangle(0, 300, 200, 100), r);
    }

    [Fact]
    public void Move_SnapsBesideAnotherFenceWithGap()
    {
        var other = new Rectangle(100, 100, 200, 200); // right edge 300, gutter line 306
        var r = FenceSnapper.Snap(new Rectangle(311, 500, 200, 100), new[] { other }, Area, 12, 6, FenceSnapper.Edges.All);
        Assert.Equal(306, r.Left);
        Assert.Equal(500, r.Top); // no y line within reach
    }

    [Fact]
    public void Move_OutsideDistance_Unchanged()
    {
        var r = FenceSnapper.Snap(new Rectangle(50, 50, 200, 100), Array.Empty<Rectangle>(), Area, 12, 6, FenceSnapper.Edges.All);
        Assert.Equal(new Rectangle(50, 50, 200, 100), r);
    }

    [Fact]
    public void Resize_MovesOnlyTheResizedEdge()
    {
        var other = new Rectangle(500, 0, 100, 100); // left edge 500, gutter 494
        var r = FenceSnapper.Snap(new Rectangle(100, 100, 390, 100), new[] { other }, Area, 12, 6, FenceSnapper.Edges.Right);
        Assert.Equal(100, r.Left);
        Assert.Equal(494, r.Right);
        Assert.Equal(100, r.Height);
    }

    [Fact]
    public void Resize_RefusesToShrinkBelowMinimum()
    {
        var other = new Rectangle(150, 0, 100, 100); // left edge 150 would make width 50
        var r = FenceSnapper.Snap(new Rectangle(100, 100, 55, 100), new[] { other }, Area, 12, 6, FenceSnapper.Edges.Right);
        Assert.Equal(new Rectangle(100, 100, 55, 100), r);
    }

    [Fact]
    public void Disabled_WhenDistanceZeroOrNoEdges()
    {
        var rect = new Rectangle(3, 3, 200, 100);
        Assert.Equal(rect, FenceSnapper.Snap(rect, Array.Empty<Rectangle>(), Area, 0, 6, FenceSnapper.Edges.All));
        Assert.Equal(rect, FenceSnapper.Snap(rect, Array.Empty<Rectangle>(), Area, 12, 6, FenceSnapper.Edges.None));
    }
}
