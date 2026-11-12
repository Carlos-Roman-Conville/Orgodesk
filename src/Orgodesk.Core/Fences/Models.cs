using System.Drawing;
using System.Text.Json.Serialization;

namespace Orgodesk.Core.Fences;

/// <summary>How the items inside a fence are ordered.</summary>
public enum SortMode
{
    Manual,
    Name,
    Type,
    Modified,
}

/// <summary>
/// A fence: a titled, shaded rectangle on the desktop that holds icons.
/// Bounds are physical pixels in virtual-screen coordinates (same space Explorer uses for icon positions).
/// </summary>
public sealed class Fence
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New Orgo";
    public Rectangle Bounds { get; set; } = new(100, 100, 300, 240);

    /// <summary>"#AARRGGBB" override for the body color, or null for the global setting.</summary>
    public string? Color { get; set; }

    /// <summary>"#AARRGGBB" override for the title bar color, or null for the global setting.</summary>
    public string? TitleColor { get; set; }

    public bool RolledUp { get; set; }

    /// <summary>Auto-roll: stay rolled up, open while the mouse is over the fence, roll back when it leaves.</summary>
    public bool AutoRollUp { get; set; }

    /// <summary>Runtime only: an auto-roll fence that is open because the mouse is over it.</summary>
    [JsonIgnore] public bool Peeking { get; set; }

    /// <summary>What is on screen: rolled up and not peeking.</summary>
    [JsonIgnore] public bool IsCollapsed => RolledUp && !Peeking;

    /// <summary>When set, the fence shows the contents of this folder instead of desktop items.</summary>
    public string? PortalPath { get; set; }

    public SortMode Sort { get; set; } = SortMode.Manual;

    /// <summary>Item keys (desktop-absolute parsing names) in display order. Unused for portals.</summary>
    public List<string> Items { get; set; } = new();

    /// <summary>Runtime only: vertical scroll of the item grid, in pixels.</summary>
    [JsonIgnore] public int ScrollOffset { get; set; }

    [JsonIgnore] public bool IsPortal => !string.IsNullOrEmpty(PortalPath);

    public Fence Clone()
    {
        var f = (Fence)MemberwiseClone();
        f.Items = new List<string>(Items);
        return f;
    }
}

/// <summary>One desktop page: its fences and the free (unfenced) icon positions.</summary>
public sealed class Page
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Page 1";
    public List<Fence> Fences { get; set; } = new();

    /// <summary>Free icons: item key to top-left position (virtual-screen pixels).</summary>
    public Dictionary<string, Point> FreeItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Page Clone()
    {
        var p = (Page)MemberwiseClone();
        p.Fences = Fences.Select(f => f.Clone()).ToList();
        p.FreeItems = new Dictionary<string, Point>(FreeItems, StringComparer.OrdinalIgnoreCase);
        return p;
    }

    public Fence? FindFence(string id) => Fences.FirstOrDefault(f => f.Id == id);

    public Fence? FenceContaining(string key) => Fences.FirstOrDefault(f => !f.IsPortal && f.Items.Contains(key, StringComparer.OrdinalIgnoreCase));

    public bool Contains(string key) => FreeItems.ContainsKey(key) || FenceContaining(key) != null;

    /// <summary>Removes the key from every fence and from the free items. Returns true if it was anywhere.</summary>
    public bool Remove(string key)
    {
        bool any = FreeItems.Remove(key);
        foreach (var f in Fences)
            any |= f.Items.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) > 0;
        return any;
    }
}

/// <summary>The whole desktop layout: pages, rules and which page is active.</summary>
public sealed class Layout
{
    public int Version { get; set; } = 1;
    public List<Page> Pages { get; set; } = new() { new Page() };
    public int ActivePageIndex { get; set; }
    public List<Rules.Rule> Rules { get; set; } = new();

    /// <summary>Named arrangements that can be applied to the active page. See <see cref="Presets"/>.</summary>
    public List<Page> Presets { get; set; } = new();
    public string? ActivePresetId { get; set; }

    /// <summary>
    /// The display scale (1.0 = 100%) the positions in this layout were laid out at; 0 = not recorded yet.
    /// When the scale changes, positions are converted so icons don't overlap (FC-32).
    /// </summary>
    public double GridScale { get; set; }

    [JsonIgnore]
    public Page ActivePage
    {
        get
        {
            if (Pages.Count == 0) Pages.Add(new Page());
            if (ActivePageIndex < 0 || ActivePageIndex >= Pages.Count) ActivePageIndex = 0;
            return Pages[ActivePageIndex];
        }
    }

    [JsonIgnore] public IEnumerable<Fence> AllFences => Pages.SelectMany(p => p.Fences);

    public Fence? FindFence(string id) => AllFences.FirstOrDefault(f => f.Id == id);

    /// <summary>The page that holds the fence, or null.</summary>
    public Page? PageOfFence(string fenceId) => Pages.FirstOrDefault(p => p.Fences.Any(f => f.Id == fenceId));

    public bool ContainsItem(string key) => Pages.Any(p => p.Contains(key));

    public Layout Clone()
    {
        var l = (Layout)MemberwiseClone();
        l.Pages = Pages.Select(p => p.Clone()).ToList();
        l.Rules = Rules.Select(r => r.Clone()).ToList();
        l.Presets = Presets.Select(p => p.Clone()).ToList();
        return l;
    }
}

/// <summary>A saved copy of a layout.</summary>
public sealed record SnapshotInfo(string Name, DateTime SavedAt, string Path);
