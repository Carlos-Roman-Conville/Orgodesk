namespace Orgodesk.Core.Fences;

/// <summary>
/// Presets: named desktop arrangements (fences plus free icon positions) that can be applied to the active page.
/// A preset is stored as a <see cref="Page"/> whose Name is the preset name. Files never move; only the layout does.
/// </summary>
public static class Presets
{
    /// <summary>Saves the active page as a preset. An existing preset with the same name is replaced.</summary>
    public static Page Save(Layout layout, string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Preset name is empty", nameof(name));
        var preset = layout.ActivePage.Clone();
        var existing = Find(layout, name);
        if (existing != null)
        {
            preset.Id = existing.Id;
            layout.Presets[layout.Presets.IndexOf(existing)] = preset;
        }
        else
        {
            preset.Id = Guid.NewGuid().ToString("N"); // the clone keeps the page's id; a preset needs its own
            layout.Presets.Add(preset);
        }
        preset.Name = name;
        layout.ActivePresetId = preset.Id;
        return preset;
    }

    /// <summary>Overwrites the preset with the active page's current arrangement.</summary>
    public static bool Update(Layout layout, string presetId)
    {
        var existing = layout.Presets.FirstOrDefault(p => p.Id == presetId);
        if (existing == null) return false;
        var fresh = layout.ActivePage.Clone();
        fresh.Id = existing.Id;
        fresh.Name = existing.Name;
        layout.Presets[layout.Presets.IndexOf(existing)] = fresh;
        layout.ActivePresetId = existing.Id;
        return true;
    }

    /// <summary>Replaces the active page's arrangement with the preset's. The caller reconciles afterwards.</summary>
    public static bool Apply(Layout layout, string presetId)
    {
        var preset = layout.Presets.FirstOrDefault(p => p.Id == presetId);
        if (preset == null) return false;
        var copy = preset.Clone();
        var page = layout.ActivePage;
        page.Fences = copy.Fences;
        page.FreeItems = copy.FreeItems;
        layout.ActivePresetId = preset.Id;
        return true;
    }

    public static bool Delete(Layout layout, string presetId)
    {
        int removed = layout.Presets.RemoveAll(p => p.Id == presetId);
        if (layout.ActivePresetId == presetId) layout.ActivePresetId = null;
        return removed > 0;
    }

    public static Page? Find(Layout layout, string name)
        => layout.Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public static Page? Active(Layout layout)
        => layout.ActivePresetId == null ? null : layout.Presets.FirstOrDefault(p => p.Id == layout.ActivePresetId);
}
