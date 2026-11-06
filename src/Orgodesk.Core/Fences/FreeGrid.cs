using System.Drawing;

namespace Orgodesk.Core.Fences;

/// <summary>Grid placement for icons that are not inside any fence.</summary>
public static class FreeGrid
{
    /// <summary>Snaps a top-left point to the nearest grid cell origin inside the area.</summary>
    public static Point Snap(Point p, Rectangle area, LayoutMetrics m)
    {
        int col = (int)Math.Round((double)(p.X - area.X) / m.CellWidth);
        int row = (int)Math.Round((double)(p.Y - area.Y) / m.CellHeight);
        int x = area.X + col * m.CellWidth;
        int y = area.Y + row * m.CellHeight;
        x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - m.CellWidth));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - m.CellHeight));
        return new Point(x, y);
    }

    public static Rectangle CellAt(Point topLeft, LayoutMetrics m) => new(topLeft, new Size(m.CellWidth, m.CellHeight));

    /// <summary>
    /// The first free grid cell, column by column from the top-left like Explorer,
    /// that does not overlap any occupied cell or any blocked rectangle (fences).
    /// </summary>
    public static Point NextFreeSlot(Rectangle area, LayoutMetrics m, IEnumerable<Rectangle> occupied, IEnumerable<Rectangle> blocked)
    {
        var occ = occupied.ToList();
        var blk = blocked.ToList();
        int cols = Math.Max(1, area.Width / m.CellWidth);
        int rows = Math.Max(1, area.Height / m.CellHeight);
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                var cell = new Rectangle(area.X + c * m.CellWidth, area.Y + r * m.CellHeight, m.CellWidth, m.CellHeight);
                if (occ.Any(o => o.IntersectsWith(cell))) continue;
                if (blk.Any(b => b.IntersectsWith(cell))) continue;
                return cell.Location;
            }
        }
        return area.Location;
    }
}
