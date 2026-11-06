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
            foreach (var key in page.FreeItems.Keys.ToList())
            {
                var p = page.FreeItems[key];
                var cell = FreeGrid.CellAt(p, m);
                if (!area.Contains(cell))
                {
                    page.FreeItems[key] = FreeGrid.Snap(new Point(
                        Math.Clamp(p.X, area.X, Math.Max(area.X, area.Right - m.CellWidth)),
                        Math.Clamp(p.Y, area.Y, Math.Max(area.Y, area.Bottom - m.CellHeight))), area, m);
                    changed++;
                }
            }
        }
        return changed;
    }
}
