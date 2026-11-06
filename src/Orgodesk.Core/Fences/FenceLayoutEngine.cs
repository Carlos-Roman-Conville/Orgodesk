using System.Drawing;

namespace Orgodesk.Core.Fences;

/// <summary>One icon's place inside a fence grid.</summary>
public readonly record struct ItemCell(string Key, Rectangle Bounds, bool Visible);

/// <summary>Pure grid layout for the items inside a fence. No UI, no shell.</summary>
public static class FenceLayoutEngine
{
    public const int MinFenceWidth = 120;
    public const int MinFenceHeight = 60;

    /// <summary>The rectangle the title bar occupies.</summary>
    public static Rectangle TitleBar(Fence fence, LayoutMetrics m)
        => new(fence.Bounds.X, fence.Bounds.Y, fence.Bounds.Width, m.TitleBarHeight);

    /// <summary>The visible rectangle of the fence (title only when rolled up).</summary>
    public static Rectangle VisibleBounds(Fence fence, LayoutMetrics m)
        => fence.IsCollapsed ? TitleBar(fence, m) : fence.Bounds;

    /// <summary>The area items are laid out in: below the title bar, inset by padding.</summary>
    public static Rectangle ContentArea(Fence fence, LayoutMetrics m)
    {
        var b = fence.Bounds;
        return new Rectangle(
            b.X + m.Padding,
            b.Y + m.TitleBarHeight + m.Padding,
            Math.Max(0, b.Width - 2 * m.Padding),
            Math.Max(0, b.Height - m.TitleBarHeight - 2 * m.Padding));
    }

    public static int Columns(Rectangle content, LayoutMetrics m)
        => Math.Max(1, content.Width / m.CellWidth);

    public static int Rows(int count, int columns)
        => columns <= 0 ? 0 : (count + columns - 1) / columns;

    public static int ContentHeight(int count, Rectangle content, LayoutMetrics m)
        => Rows(count, Columns(content, m)) * m.CellHeight;

    public static int MaxScroll(int count, Rectangle content, LayoutMetrics m)
        => Math.Max(0, ContentHeight(count, content, m) - content.Height);

    public static int ClampScroll(int scroll, int count, Rectangle content, LayoutMetrics m)
        => Math.Clamp(scroll, 0, MaxScroll(count, content, m));

    /// <summary>Lays the keys out left to right, top to bottom, applying the scroll offset.</summary>
    public static IReadOnlyList<ItemCell> Arrange(IReadOnlyList<string> keys, Rectangle content, LayoutMetrics m, int scrollOffset)
    {
        var cols = Columns(content, m);
        var result = new List<ItemCell>(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            int col = i % cols, row = i / cols;
            var r = new Rectangle(
                content.X + col * m.CellWidth,
                content.Y + row * m.CellHeight - scrollOffset,
                m.CellWidth, m.CellHeight);
            bool visible = r.Bottom > content.Y && r.Top < content.Bottom;
            result.Add(new ItemCell(keys[i], r, visible));
        }
        return result;
    }

    /// <summary>
    /// The insertion index for a drop at <paramref name="p"/> (same coordinate space as content).
    /// Points past the last item return <paramref name="count"/>.
    /// </summary>
    public static int InsertionIndex(Point p, int count, Rectangle content, LayoutMetrics m, int scrollOffset)
    {
        if (count == 0) return 0;
        var cols = Columns(content, m);
        int relX = p.X - content.X;
        int relY = p.Y - content.Y + scrollOffset;
        int row = Math.Max(0, relY / m.CellHeight);
        // Halfway across a cell counts as "after" it.
        int col = (int)Math.Round((double)relX / m.CellWidth);
        col = Math.Clamp(col, 0, cols);
        int idx = row * cols + col;
        return Math.Clamp(idx, 0, count);
    }

    /// <summary>Clamps a fence inside the given area and enforces the minimum size.</summary>
    public static Rectangle ClampBounds(Rectangle bounds, Rectangle area, LayoutMetrics m)
    {
        int minH = Math.Max(MinFenceHeight, m.TitleBarHeight + m.CellHeight + 2 * m.Padding);
        int w = Math.Min(Math.Max(MinFenceWidth, bounds.Width), Math.Max(MinFenceWidth, area.Width));
        int h = Math.Min(Math.Max(minH, bounds.Height), Math.Max(minH, area.Height));
        int x = Math.Clamp(bounds.X, area.X, Math.Max(area.X, area.Right - w));
        int y = Math.Clamp(bounds.Y, area.Y, Math.Max(area.Y, area.Bottom - h));
        return new Rectangle(x, y, w, h);
    }
}
