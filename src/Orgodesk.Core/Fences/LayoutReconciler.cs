using System.Drawing;
using Orgodesk.Core.Rules;

namespace Orgodesk.Core.Fences;

/// <summary>Keeps the layout in step with what is actually on the desktop.</summary>
public static class LayoutReconciler
{
    public sealed record Result(IReadOnlyList<string> Added, IReadOnlyList<string> Removed)
    {
        public bool Changed => Added.Count > 0 || Removed.Count > 0;
    }

    /// <summary>
    /// Removes items that no longer exist, and places new ones: by rule if one matches,
    /// otherwise in the next free grid slot on the active page.
    /// </summary>
    public static Result Reconcile(Layout layout, IReadOnlyList<ItemFacts> current, Rectangle area, LayoutMetrics m)
    {
        var present = new HashSet<string>(current.Select(c => c.Key), StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();
        foreach (var page in layout.Pages)
        {
            foreach (var key in page.FreeItems.Keys.ToList())
                if (!present.Contains(key)) { page.FreeItems.Remove(key); removed.Add(key); }
            foreach (var fence in page.Fences)
                foreach (var key in fence.Items.ToList())
                    if (!present.Contains(key)) { fence.Items.Remove(key); removed.Add(key); }
        }

        var added = new List<string>();
        foreach (var item in current)
        {
            if (layout.ContainsItem(item.Key)) continue;
            Place(layout, item, area, m, respectExisting: true);
            added.Add(item.Key);
        }
        return new Result(added, removed.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Moves every item that matches a rule into its target fence. Returns how many moved.</summary>
    public static int ApplyRules(Layout layout, IReadOnlyList<ItemFacts> current)
    {
        int moved = 0;
        foreach (var item in current)
        {
            var target = RulesEngine.Match(layout.Rules, item);
            if (target == null) continue;
            var fence = layout.FindFence(target);
            if (fence == null || fence.IsPortal) continue;
            if (fence.Items.Contains(item.Key, StringComparer.OrdinalIgnoreCase)) continue;
            foreach (var p in layout.Pages) p.Remove(item.Key);
            fence.Items.Add(item.Key);
            moved++;
        }
        return moved;
    }

    /// <summary>Places one item: by rule into its fence, or into the next free slot of the active page.</summary>
    public static void Place(Layout layout, ItemFacts item, Rectangle area, LayoutMetrics m, bool respectExisting)
    {
        if (respectExisting && layout.ContainsItem(item.Key)) return;
        var target = RulesEngine.Match(layout.Rules, item);
        if (target != null)
        {
            var fence = layout.FindFence(target);
            if (fence != null && !fence.IsPortal)
            {
                fence.Items.Add(item.Key);
                return;
            }
        }
        var page = layout.ActivePage;
        page.FreeItems[item.Key] = NextFreeSlot(page, area, m);
    }

    public static Point NextFreeSlot(Page page, Rectangle area, LayoutMetrics m) =>
        FreeGrid.NextFreeSlot(area, m,
            page.FreeItems.Values.Select(p => FreeGrid.CellAt(p, m)),
            page.Fences.Select(f => FenceLayoutEngine.VisibleBounds(f, m)));

    /// <summary>Deletes a fence and returns its items to the free desktop of its page.</summary>
    public static void ReleaseFence(Page page, Fence fence, Rectangle area, LayoutMetrics m)
    {
        page.Fences.Remove(fence);
        foreach (var key in fence.Items)
            page.FreeItems[key] = NextFreeSlot(page, area, m);
        fence.Items.Clear();
    }

    /// <summary>
    /// Moves free icons that sit under any fence on the page to the next free grid slot.
    /// Fences never leave icons under a fence. Returns how many moved.
    /// </summary>
    public static int EvictFreeIconsUnderFences(Page page, Rectangle area, LayoutMetrics m)
    {
        var covers = page.Fences.Select(f => FenceLayoutEngine.VisibleBounds(f, m)).ToList();
        int moved = 0;
        foreach (var key in page.FreeItems.Keys.ToList())
        {
            var cell = FreeGrid.CellAt(page.FreeItems[key], m);
            if (!covers.Any(c => c.IntersectsWith(cell))) continue;
            page.FreeItems.Remove(key);
            page.FreeItems[key] = NextFreeSlot(page, area, m);
            moved++;
        }
        return moved;
    }

    /// <summary>Pulls every fence and free icon back inside the area (after a resolution or monitor change).</summary>
    public static int ClampToArea(Layout layout, Rectangle area, LayoutMetrics m)
    {
        int changed = 0;
        foreach (var page in layout.Pages)
        {
            foreach (var f in page.Fences)
            {
                var nb = FenceLayoutEngine.ClampBounds(f.Bounds, area, m);
                if (nb != f.Bounds) { f.Bounds = nb; changed++; }
            }
            var outside = page.FreeItems.Where(kv => !area.Contains(FreeGrid.CellAt(kv.Value, m))).Select(kv => kv.Key).ToList();
            foreach (var key in outside)
            {
                var p = page.FreeItems[key];
                page.FreeItems.Remove(key);
                var snapped = FreeGrid.Snap(new Point(
                    Math.Clamp(p.X, area.X, Math.Max(area.X, area.Right - m.CellWidth)),
                    Math.Clamp(p.Y, area.Y, Math.Max(area.Y, area.Bottom - m.CellHeight))), area, m);
                // FC-33: never drop a pulled-back icon on top of another icon or under a fence.
                page.FreeItems[key] = IsFree(page, snapped, m) ? snapped : NextFreeSlot(page, area, m);
                changed++;
            }
            changed += SpreadOverlaps(page, area, m);
        }
        return changed;
    }

    /// <summary>
    /// Moves icons that share a cell (or sit under a fence) to free cells, if any are left. Used after display changes,
    /// so icons stacked while the screen was full spread out again when it gets bigger. Returns how many moved.
    /// </summary>
    public static int SpreadOverlaps(Page page, Rectangle area, LayoutMetrics m)
    {
        // Pass 1: in grid order, an icon keeps its cell unless an earlier kept icon or a fence already covers it.
        var all = page.FreeItems.OrderBy(kv => kv.Value.X).ThenBy(kv => kv.Value.Y).ToList();
        page.FreeItems.Clear();
        var movers = new List<(string Key, Point Old)>();
        foreach (var (key, p) in all)
        {
            if (IsFree(page, p, m)) page.FreeItems[key] = p;
            else movers.Add((key, p));
        }
        // Pass 2: the rest go to free cells; with no room left, an icon stays where it was.
        int moved = 0;
        foreach (var (key, old) in movers)
        {
            var slot = NextFreeSlot(page, area, m);
            if (IsFree(page, slot, m)) { page.FreeItems[key] = slot; moved++; }
            else page.FreeItems[key] = old;
        }
        return moved;
    }

    /// <summary>
    /// Converts every page's and preset's fence bounds and free icon positions from the scale the layout was laid out at
    /// (<see cref="Layout.GridScale"/>) to <paramref name="scale"/>, then moves any icon that would overlap to a free cell.
    /// A layout with no recorded scale is only stamped with the current one. Returns true if the layout changed.
    /// </summary>
    public static bool Rescale(Layout layout, double scale, Rectangle area, LayoutMetrics m)
    {
        if (scale <= 0) return false;
        if (layout.GridScale <= 0) { layout.GridScale = scale; return true; }
        double ratio = scale / layout.GridScale;
        if (Math.Abs(ratio - 1) < 0.01) return false;

        int S(int v, int origin) => origin + (int)Math.Round((v - origin) * ratio);
        foreach (var page in layout.Pages.Concat(layout.Presets))
        {
            foreach (var f in page.Fences)
            {
                var b = f.Bounds;
                f.Bounds = FenceLayoutEngine.ClampBounds(new Rectangle(S(b.X, area.X), S(b.Y, area.Y),
                    (int)Math.Round(b.Width * ratio), (int)Math.Round(b.Height * ratio)), area, m);
            }
            // Re-place free icons in reading order (column by column, like the grid fills) so earlier ones keep their spots.
            var items = page.FreeItems.OrderBy(kv => kv.Value.X).ThenBy(kv => kv.Value.Y).ToList();
            page.FreeItems.Clear();
            foreach (var (key, p) in items)
            {
                var target = FreeGrid.Snap(new Point(S(p.X, area.X), S(p.Y, area.Y)), area, m);
                page.FreeItems[key] = IsFree(page, target, m) ? target : NextFreeSlot(page, area, m);
            }
        }
        layout.GridScale = scale;
        return true;
    }

    /// <summary>True if a free icon at <paramref name="topLeft"/> would overlap no other free icon and no fence.</summary>
    private static bool IsFree(Page page, Point topLeft, LayoutMetrics m)
    {
        var cell = FreeGrid.CellAt(topLeft, m);
        return !page.FreeItems.Values.Any(p => FreeGrid.CellAt(p, m).IntersectsWith(cell))
            && !page.Fences.Any(f => FenceLayoutEngine.VisibleBounds(f, m).IntersectsWith(cell));
    }
}
