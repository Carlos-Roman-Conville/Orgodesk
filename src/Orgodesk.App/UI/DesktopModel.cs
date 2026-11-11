using System.Drawing;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Persistence;
using Orgodesk.Core.Rules;
using Orgodesk.Windows.Shell;

namespace Orgodesk.App.UI;

/// <summary>Identifies an item on screen: a desktop item (PortalId null) or an item inside a portal fence.</summary>
public readonly record struct ItemRef(string? PortalId, string Key);

/// <summary>Where a dragged item will land, for the renderer and the drop handler.</summary>
public sealed record DropTarget(Fence? Fence, int InsertIndex, Point? FreePosition, bool Valid);

/// <summary>Where a not-yet-visible item (after a move or rename) should appear once the shell reports it.</summary>
public sealed record PendingPlacement(string? FenceId, Point? FreePosition)
{
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public bool Expired => (DateTime.UtcNow - Created) > TimeSpan.FromSeconds(45);
}

/// <summary>
/// Runtime state: the layout, the settings, the live shell items (desktop + portals) and the icon cache.
/// Everything here is UI-thread only.
/// </summary>
public sealed class DesktopModel : IDisposable
{
    public Layout Layout { get; set; }
    public AppSettings Settings { get; set; }
    public IconProvider Icons { get; } = new();

    public Dictionary<string, ShellItemInfo> DesktopItems { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<ShellItemInfo>> PortalItems { get; } = new();
    public Dictionary<string, PendingPlacement> Pending { get; } = new(StringComparer.OrdinalIgnoreCase);

    public DesktopModel(Layout layout, AppSettings settings)
    {
        Layout = layout;
        Settings = settings;
    }

    public LayoutMetrics BaseMetrics => new(Settings.CellWidth, Settings.CellHeight, Settings.IconSize, Settings.TitleBarHeight, Settings.FencePadding);

    /// <summary>Re-reads the desktop. Returns the facts for reconciliation.</summary>
    public List<ItemFacts> RefreshDesktop()
    {
        var fresh = ShellEnumerator.EnumerateDesktop(PlacedKeys());
        foreach (var old in DesktopItems.Values) old.Dispose();
        DesktopItems.Clear();
        foreach (var it in fresh)
        {
            if (DesktopItems.ContainsKey(it.Key)) { it.Dispose(); continue; }
            DesktopItems[it.Key] = it;
        }
        return DesktopItems.Values.Select(i => i.ToFacts()).ToList();
    }

    /// <summary>Every item key placed in an Orgo or on the desktop, in any page or preset.</summary>
    private HashSet<string> PlacedKeys()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in Layout.Pages.Concat(Layout.Presets))
        {
            foreach (var k in page.FreeItems.Keys) keys.Add(k);
            foreach (var f in page.Fences) if (!f.IsPortal) foreach (var k in f.Items) keys.Add(k);
        }
        return keys;
    }

    /// <summary>Re-reads every portal folder in the layout.</summary>
    public void RefreshPortals()
    {
        foreach (var list in PortalItems.Values) foreach (var it in list) it.Dispose();
        PortalItems.Clear();
        foreach (var f in Layout.AllFences.Where(f => f.IsPortal))
        {
            try { PortalItems[f.Id] = ShellEnumerator.EnumerateFolder(f.PortalPath!); }
            catch { PortalItems[f.Id] = new List<ShellItemInfo>(); }
        }
    }

    public ShellItemInfo? Item(ItemRef r)
    {
        if (r.PortalId == null) return DesktopItems.TryGetValue(r.Key, out var d) ? d : null;
        return PortalItems.TryGetValue(r.PortalId, out var list)
            ? list.FirstOrDefault(i => string.Equals(i.Key, r.Key, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    /// <summary>The items a fence shows, in display order (sorted by the fence's sort mode).</summary>
    public List<ShellItemInfo> ItemsOf(Fence fence)
    {
        IEnumerable<ShellItemInfo> items;
        if (fence.IsPortal)
        {
            items = PortalItems.TryGetValue(fence.Id, out var list) ? list : Enumerable.Empty<ShellItemInfo>();
            return Sort(items, fence.Sort == SortMode.Manual ? SortMode.Name : fence.Sort).ToList();
        }
        items = fence.Items.Select(k => DesktopItems.TryGetValue(k, out var i) ? i : null).Where(i => i != null)!;
        return Sort(items, fence.Sort).ToList();
    }

    public static IEnumerable<ShellItemInfo> Sort(IEnumerable<ShellItemInfo> items, SortMode mode) => mode switch
    {
        SortMode.Name => items.OrderBy(i => i.IsFolder ? 0 : 1).ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase),
        SortMode.Type => items.OrderBy(i => i.TypeSortKey, StringComparer.CurrentCultureIgnoreCase),
        SortMode.Modified => items.OrderByDescending(i => i.Modified ?? DateTime.MinValue).ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase),
        _ => items,
    };

    /// <summary>Every item visible on the active page: free icons and the contents of all fences.</summary>
    public IEnumerable<ItemRef> VisibleRefs()
    {
        var page = Layout.ActivePage;
        foreach (var k in page.FreeItems.Keys) if (DesktopItems.ContainsKey(k)) yield return new ItemRef(null, k);
        foreach (var f in page.Fences)
            foreach (var it in ItemsOf(f))
                yield return new ItemRef(f.IsPortal ? f.Id : null, it.Key);
    }

    public IEnumerable<string> PortalPaths() => Layout.AllFences.Where(f => f.IsPortal).Select(f => f.PortalPath!);

    public void Dispose()
    {
        foreach (var i in DesktopItems.Values) i.Dispose();
        DesktopItems.Clear();
        foreach (var list in PortalItems.Values) foreach (var it in list) it.Dispose();
        PortalItems.Clear();
        Icons.Dispose();
    }
}
