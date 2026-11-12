using System.Text.Json;
using Orgodesk.Core.Fences;

namespace Orgodesk.Core.Persistence;

/// <summary>
/// Reads and writes the layout, settings and snapshots as JSON under one root folder.
/// Writes are atomic: a temp file is written and then moved over the target.
/// </summary>
public sealed class LayoutStore
{
    public string RootDir { get; }
    public string LayoutPath => Path.Combine(RootDir, "layout.json");
    public string SettingsPath => Path.Combine(RootDir, "settings.json");
    public string SnapshotsDir => Path.Combine(RootDir, "snapshots");
    /// <summary>The last layout.json that loaded cleanly, refreshed at every clean load (FC-28).</summary>
    public string LayoutBackupPath => LayoutPath + ".bak";

    /// <summary>
    /// Problems met while loading (a damaged file, a restore from backup). The app logs them and tells the user;
    /// <see cref="TakeLoadProblems"/> returns them once.
    /// </summary>
    private readonly List<string> _loadProblems = new();

    public IReadOnlyList<string> TakeLoadProblems()
    {
        var list = _loadProblems.ToList();
        _loadProblems.Clear();
        return list;
    }

    public LayoutStore(string rootDir)
    {
        RootDir = rootDir;
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(SnapshotsDir);
    }

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "orgodesk");

    /// <summary>Moves the data folder of an earlier name (fences-clone) to the current one. Nothing is deleted.</summary>
    public static void MigrateLegacyRoot()
    {
        var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "fences-clone");
        try
        {
            if (Directory.Exists(old) && !Directory.Exists(DefaultRoot)) Directory.Move(old, DefaultRoot);
        }
        catch { /* best effort; a fresh folder is created instead */ }
    }

    // ---- layout ----

    public Layout LoadLayout()
    {
        var l = ReadJson<Layout>(LayoutPath, out var damagedCopy);
        if (l != null)
        {
            // Keep the last good layout so a damaged file can be recovered (FC-28).
            try { File.Copy(LayoutPath, LayoutBackupPath, overwrite: true); } catch { /* best effort */ }
        }
        else if (damagedCopy != null)
        {
            var backup = ReadJson<Layout>(LayoutBackupPath, out _);
            if (backup != null)
            {
                l = backup;
                _loadProblems.Add($"layout.json was damaged and could not be read. Your Orgos were restored from the last good copy " +
                    $"({File.GetLastWriteTime(LayoutBackupPath):yyyy-MM-dd HH:mm}). The damaged file was kept as {Path.GetFileName(damagedCopy)}.");
                SaveLayout(l);
            }
            else
            {
                _loadProblems.Add($"layout.json was damaged and could not be read, and there was no backup to restore, so Orgodesk started " +
                    $"with an empty layout. The damaged file was kept as {Path.GetFileName(damagedCopy)} in {RootDir}.");
            }
        }
        if (l == null) l = new Layout();
        Normalize(l);
        return l;
    }

    public void SaveLayout(Layout layout)
    {
        Normalize(layout);
        WriteJsonAtomic(LayoutPath, layout);
    }

    // ---- settings ----

    public AppSettings LoadSettings()
    {
        var s = ReadJson<AppSettings>(SettingsPath, out var damagedCopy);
        if (s != null) return s;
        s = new AppSettings();
        if (damagedCopy != null)
        {
            // Write the defaults back so the damaged file isn't copied aside again at every start (FC-29).
            try { SaveSettings(s); } catch { /* best effort */ }
            _loadProblems.Add($"settings.json was damaged, so settings were reset to defaults. The damaged file was kept as {Path.GetFileName(damagedCopy)}.");
        }
        return s;
    }

    public void SaveSettings(AppSettings settings) => WriteJsonAtomic(SettingsPath, settings);

    // ---- snapshots ----

    public IReadOnlyList<SnapshotInfo> ListSnapshots()
    {
        var list = new List<SnapshotInfo>();
        foreach (var file in Directory.EnumerateFiles(SnapshotsDir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            list.Add(new SnapshotInfo(name, File.GetLastWriteTime(file), file));
        }
        return list.OrderByDescending(s => s.SavedAt).ToList();
    }

    public SnapshotInfo SaveSnapshot(string name, Layout layout)
    {
        var safe = SanitizeName(name);
        if (safe.Length == 0) safe = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
        var path = Path.Combine(SnapshotsDir, safe + ".json");
        WriteJsonAtomic(path, layout);
        return new SnapshotInfo(safe, File.GetLastWriteTime(path), path);
    }

    public Layout? LoadSnapshot(string name)
    {
        var path = Path.Combine(SnapshotsDir, SanitizeName(name) + ".json");
        var l = ReadJson<Layout>(path, out _);
        if (l != null) Normalize(l);
        return l;
    }

    public bool DeleteSnapshot(string name)
    {
        var path = Path.Combine(SnapshotsDir, SanitizeName(name) + ".json");
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    // ---- helpers ----

    public static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    private static void Normalize(Layout l)
    {
        if (l.Pages.Count == 0) l.Pages.Add(new Page());
        if (l.ActivePageIndex < 0 || l.ActivePageIndex >= l.Pages.Count) l.ActivePageIndex = 0;
        for (int i = 0; i < l.Pages.Count; i++)
        {
            var p = l.Pages[i];
            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = $"Page {i + 1}";
            if (p.FreeItems.Comparer != StringComparer.OrdinalIgnoreCase)
                p.FreeItems = new Dictionary<string, System.Drawing.Point>(p.FreeItems, StringComparer.OrdinalIgnoreCase);
        }
        // Presets saved before 2026-11-06 could share one id; give duplicates their own.
        var presetIds = new HashSet<string>();
        foreach (var p in l.Presets)
            if (!presetIds.Add(p.Id)) p.Id = Guid.NewGuid().ToString("N");
        // An item may only live in one place; keep the first occurrence.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in l.Pages)
        {
            foreach (var f in p.Fences)
                f.Items = f.Items.Where(k => seen.Add(k)).ToList();
            foreach (var k in p.FreeItems.Keys.ToList())
                if (!seen.Add(k)) p.FreeItems.Remove(k);
        }
    }

    /// <summary>Reads a JSON file. A damaged file is copied aside and its copy's path returned in <paramref name="damagedCopy"/>.</summary>
    private static T? ReadJson<T>(string path, out string? damagedCopy) where T : class
    {
        damagedCopy = null;
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonDefaults.Options);
        }
        catch (Exception ex) when (ex is JsonException || ex is IOException)
        {
            // Keep the broken file for inspection and start fresh.
            var backup = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Copy(path, backup, overwrite: true); damagedCopy = backup; } catch { damagedCopy = path; }
            return null;
        }
    }

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(value, JsonDefaults.Options);
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* best effort */ }   // FC-31: no stray .tmp after a failed save
            throw;
        }
    }
}
