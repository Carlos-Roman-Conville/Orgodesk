using System.Drawing;

namespace Orgodesk.Core.Fences;

/// <summary>Snaps a fence being moved or resized to the screen edges and to other fences.</summary>
public static class FenceSnapper
{
    [Flags]
    public enum Edges { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8, All = 15 }

    /// <summary>
    /// Returns <paramref name="rect"/> adjusted so that any of its <paramref name="movable"/> edges within
    /// <paramref name="distance"/> pixels of a snap line lands on it. Snap lines are the area's edges and every
    /// other fence's edges, plus the lines <paramref name="gap"/> pixels outside other fences so fences sit side by
    /// side with a small gutter. When moving (all edges movable) the whole rectangle shifts; when resizing only the
    /// named edges move.
    /// </summary>
    public static Rectangle Snap(Rectangle rect, IEnumerable<Rectangle> others, Rectangle area, int distance, int gap, Edges movable)
    {
        if (movable == Edges.None || distance <= 0) return rect;
        var xs = new List<int> { area.Left, area.Right };
        var ys = new List<int> { area.Top, area.Bottom };
        foreach (var o in others)
        {
            xs.AddRange(new[] { o.Left, o.Right, o.Left - gap, o.Right + gap });
            ys.AddRange(new[] { o.Top, o.Bottom, o.Top - gap, o.Bottom + gap });
        }

        bool moving = movable == Edges.All;
        int l = rect.Left, t = rect.Top, r = rect.Right, b = rect.Bottom;

        if (moving)
        {
            int dx = Best(Delta(l, xs), Delta(r, xs), distance);
            int dy = Best(Delta(t, ys), Delta(b, ys), distance);
            return new Rectangle(l + dx, t + dy, rect.Width, rect.Height);
        }

        if (movable.HasFlag(Edges.Left)) { int d = Delta(l, xs); if (Math.Abs(d) <= distance) l += d; }
        if (movable.HasFlag(Edges.Right)) { int d = Delta(r, xs); if (Math.Abs(d) <= distance) r += d; }
        if (movable.HasFlag(Edges.Top)) { int d = Delta(t, ys); if (Math.Abs(d) <= distance) t += d; }
        if (movable.HasFlag(Edges.Bottom)) { int d = Delta(b, ys); if (Math.Abs(d) <= distance) b += d; }
        if (r - l < FenceLayoutEngine.MinFenceWidth || b - t < FenceLayoutEngine.MinFenceHeight) return rect;
        return Rectangle.FromLTRB(l, t, r, b);
    }

    /// <summary>The signed distance from <paramref name="value"/> to the nearest line (int.MaxValue if none).</summary>
    private static int Delta(int value, List<int> lines)
    {
        int best = int.MaxValue;
        foreach (var line in lines)
        {
            int d = line - value;
            if (Math.Abs(d) < Math.Abs(best)) best = d;
        }
        return best;
    }

    private static int Best(int a, int b, int distance)
    {
        int d = Math.Abs(a) <= Math.Abs(b) ? a : b;
        return Math.Abs(d) <= distance ? d : 0;
    }
}
