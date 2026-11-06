using System.Drawing;
using Orgodesk.Core.Fences;
using Xunit;

namespace Orgodesk.Core.Tests;

public class FenceLayoutEngineTests
{
    private static readonly LayoutMetrics M = new(80, 90, 48, 24, 4);

    [Fact]
    public void ContentArea_IsBelowTitleAndInsetByPadding()
    {
        var f = new Fence { Bounds = new Rectangle(10, 20, 300, 200) };
        var c = FenceLayoutEngine.ContentArea(f, M);
        Assert.Equal(new Rectangle(14, 48, 292, 168), c);
    }

    [Fact]
    public void Columns_NeverBelowOne()
    {
        Assert.Equal(1, FenceLayoutEngine.Columns(new Rectangle(0, 0, 10, 10), M));
        Assert.Equal(3, FenceLayoutEngine.Columns(new Rectangle(0, 0, 250, 10), M));
    }

    [Fact]
    public void Arrange_FillsRowsLeftToRight()
    {
        var content = new Rectangle(0, 0, 250, 300); // 3 columns
        var keys = new[] { "a", "b", "c", "d" };
        var cells = FenceLayoutEngine.Arrange(keys, content, M, 0);
        Assert.Equal(4, cells.Count);
        Assert.Equal(new Rectangle(0, 0, 80, 90), cells[0].Bounds);
        Assert.Equal(new Rectangle(160, 0, 80, 90), cells[2].Bounds);
        Assert.Equal(new Rectangle(0, 90, 80, 90), cells[3].Bounds);
        Assert.All(cells, c => Assert.True(c.Visible));
    }

    [Fact]
    public void Arrange_MarksScrolledOutCellsInvisible()
    {
        var content = new Rectangle(0, 0, 80, 100); // 1 column, ~1 row visible
        var keys = new[] { "a", "b", "c" };
        var cells = FenceLayoutEngine.Arrange(keys, content, M, 90);
        Assert.False(cells[0].Visible); // scrolled above
        Assert.True(cells[1].Visible);
        Assert.True(cells[2].Visible); // partially visible at y=90..180 with content bottom 100
    }

    [Fact]
    public void MaxScroll_IsZeroWhenEverythingFits()
    {
        var content = new Rectangle(0, 0, 250, 300);
        Assert.Equal(0, FenceLayoutEngine.MaxScroll(3, content, M));
        Assert.Equal(90 * 4 - 300, FenceLayoutEngine.MaxScroll(12, content, M));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(100, 10, 1)]   // past the middle of cell 0 -> after it
    [InlineData(30, 10, 0)]
    [InlineData(30, 100, 3)]   // second row, first column
    [InlineData(1000, 1000, 5)]
    public void InsertionIndex_MapsPointsToSlots(int x, int y, int expected)
    {
        var content = new Rectangle(0, 0, 250, 300); // 3 columns
        Assert.Equal(expected, FenceLayoutEngine.InsertionIndex(new Point(x, y), 5, content, M, 0));
    }

    [Fact]
    public void ClampBounds_KeepsFenceInsideArea()
    {
        var area = new Rectangle(0, 0, 1000, 800);
        var r = FenceLayoutEngine.ClampBounds(new Rectangle(950, 790, 300, 200), area, M);
        Assert.Equal(new Rectangle(700, 600, 300, 200), r);
        var tiny = FenceLayoutEngine.ClampBounds(new Rectangle(5, 5, 10, 10), area, M);
        Assert.True(tiny.Width >= FenceLayoutEngine.MinFenceWidth);
        Assert.True(tiny.Height >= M.TitleBarHeight + M.CellHeight + 2 * M.Padding);
    }

    [Fact]
    public void VisibleBounds_IsTitleOnlyWhenRolledUp()
    {
        var f = new Fence { Bounds = new Rectangle(10, 20, 300, 200), RolledUp = true };
        Assert.Equal(new Rectangle(10, 20, 300, 24), FenceLayoutEngine.VisibleBounds(f, M));
    }
}
