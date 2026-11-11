using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Orgodesk.Windows.Shell;

/// <summary>Enumerates the desktop namespace (user + public desktop + special items) and arbitrary folders.</summary>
public static class ShellEnumerator
{
    private static readonly Dictionary<string, bool> DefaultSpecialVisible = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{645FF040-5081-101B-9F08-00AA002F954E}"] = true,   // Recycle Bin
        ["{20D04FE0-3AEA-1069-A2D8-08002B30309D}"] = false,  // This PC
        ["{59031a47-3f72-44a7-89c5-5595fe6b30ee}"] = false,  // User's Files
        ["{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}"] = false,  // Network
        ["{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}"] = false,  // Control Panel
        ["{018D5C66-4533-4307-9B53-224DE2ED1FE6}"] = true,   // OneDrive (shown when installed)
    };

    public static IShellFolder DesktopFolder
    {
        get
        {
            Marshal.ThrowExceptionForHR(Shell32.SHGetDesktopFolder(out var f));
            return f;
        }
    }

    /// <summary>The PIDL of the desktop root (virtual folder). Caller frees it.</summary>
    public static IntPtr DesktopRootPidl()
    {
        Marshal.ThrowExceptionForHR(Shell32.SHGetSpecialFolderLocation(IntPtr.Zero, Const.CSIDL_DESKTOP, out var pidl));
        return pidl;
    }

    /// <summary>
    /// Everything the real desktop would show, honoring the hidden-file and desktop-icon settings.
    /// Items in <paramref name="placed"/> (already in an Orgo or on the desktop in some page or preset) are always kept,
    /// so a user who put Home or Gallery into an Orgo keeps them.
    /// </summary>
    public static List<ShellItemInfo> EnumerateDesktop(ISet<string>? placed = null)
    {
        var folder = DesktopFolder;
        var items = Enumerate(folder, IntPtr.Zero, HiddenFlags());
        var hidden = HiddenDesktopIcons();
        items.RemoveAll(i =>
        {
            if (placed != null && placed.Contains(i.Key)) return false;
            if (!i.Key.StartsWith("::", StringComparison.Ordinal)) return false;
            var clsid = i.Key.Substring(2);
            if (hidden.TryGetValue(clsid, out var isHidden)) { if (isHidden) { i.Dispose(); return true; } return false; }
            if (DefaultSpecialVisible.TryGetValue(clsid, out var shown) && !shown) { i.Dispose(); return true; }
            return false;
        });

        // FC-25: the desktop root also returns navigation-pane items (Home, Gallery, Libraries, user folders, drives...).
        // Keep only what Explorer's own desktop shows, plus files in the desktop folders (so a file created a moment ago
        // is never dropped while Explorer's view catches up), plus anything already placed.
        // If Explorer's view can't be read, keep everything, as before.
        var explorer = ExplorerDesktopView.VisibleKeys();
        if (explorer != null)
        {
            var desktopDirs = DesktopDirectories();
            items.RemoveAll(i =>
            {
                if (placed != null && placed.Contains(i.Key)) return false;
                if (explorer.Contains(i.Key)) return false;
                if (i.Path != null && desktopDirs.Contains(System.IO.Path.GetDirectoryName(i.Path.TrimEnd('\\')) ?? "")) return false;
                if (i.Key.StartsWith("::", StringComparison.Ordinal) && DefaultSpecialVisible.ContainsKey(i.Key.Substring(2))) return false;
                i.Dispose();
                return true;
            });
        }
        return items;
    }

    private static HashSet<string> DesktopDirectories()
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory })
        {
            var d = Environment.GetFolderPath(f);
            if (!string.IsNullOrEmpty(d)) dirs.Add(d.TrimEnd('\\'));
        }
        return dirs;
    }

    /// <summary>The items of a filesystem folder, as a portal shows them.</summary>
    public static List<ShellItemInfo> EnumerateFolder(string path)
    {
        if (!Directory.Exists(path)) return new List<ShellItemInfo>();
        Marshal.ThrowExceptionForHR(Shell32.SHParseDisplayName(path, IntPtr.Zero, out var folderPidl, 0, out _));
        try
        {
            var desktop = DesktopFolder;
            var iid = Iids.IShellFolder;
            desktop.BindToObject(folderPidl, IntPtr.Zero, ref iid, out var ppv);
            var folder = (IShellFolder)Marshal.GetObjectForIUnknown(ppv);
            Marshal.Release(ppv);
            return Enumerate(folder, folderPidl, HiddenFlags());
        }
        finally
        {
            Shell32.ILFree(folderPidl);
        }
    }

    /// <summary>Parses a path (or shell parsing name) into an absolute PIDL. Caller frees it.</summary>
    public static IntPtr ParsePidl(string parsingName)
    {
        return Shell32.SHParseDisplayName(parsingName, IntPtr.Zero, out var pidl, 0, out _) == 0 ? pidl : IntPtr.Zero;
    }

    private static List<ShellItemInfo> Enumerate(IShellFolder folder, IntPtr folderPidl, uint extraFlags)
    {
        var result = new List<ShellItemInfo>();
        int hr = folder.EnumObjects(IntPtr.Zero, Const.SHCONTF_FOLDERS | Const.SHCONTF_NONFOLDERS | extraFlags, out var e);
        if (hr != Const.S_OK || e == null) return result;
        var one = new IntPtr[1];
        while (e.Next(1, out var child, out var fetched) == Const.S_OK && fetched == 1)
        {
            IntPtr absolute = folderPidl == IntPtr.Zero ? Shell32.ILClone(child) : Shell32.ILCombine(folderPidl, child);
            try
            {
                one[0] = child;
                uint attrs = Const.SFGAO_FOLDER | Const.SFGAO_LINK | Const.SFGAO_FILESYSTEM | Const.SFGAO_STREAM
                           | Const.SFGAO_CANRENAME | Const.SFGAO_CANDELETE;
                try { folder.GetAttributesOf(1, one, ref attrs); } catch { attrs = 0; }

                var key = Shell32.GetNameFromIDList(absolute, Const.SIGDN_DESKTOPABSOLUTEPARSING);
                var name = Shell32.GetNameFromIDList(absolute, Const.SIGDN_NORMALDISPLAY) ?? key ?? "?";
                if (key == null) { Shell32.ILFree(child); Shell32.ILFree(absolute); continue; }
                string? path = (attrs & Const.SFGAO_FILESYSTEM) != 0 ? Shell32.GetNameFromIDList(absolute, Const.SIGDN_FILESYSPATH) : null;
                DateTime? modified = null;
                if (path != null)
                {
                    try
                    {
                        modified = (attrs & Const.SFGAO_FOLDER) != 0 && (attrs & Const.SFGAO_STREAM) == 0
                            ? Directory.GetLastWriteTime(path)
                            : File.GetLastWriteTime(path);
                    }
                    catch { }
                }
                result.Add(new ShellItemInfo(folder, child, absolute, key, name, path, attrs, modified));
                child = IntPtr.Zero; absolute = IntPtr.Zero;
            }
            finally
            {
                if (child != IntPtr.Zero) Shell32.ILFree(child);
                if (absolute != IntPtr.Zero) Shell32.ILFree(absolute);
            }
        }
        return result;
    }

    private static uint HiddenFlags()
    {
        uint flags = 0;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            if (k?.GetValue("Hidden") is int h && h == 1) flags |= Const.SHCONTF_INCLUDEHIDDEN;
            if (k?.GetValue("ShowSuperHidden") is int s && s == 1) flags |= Const.SHCONTF_INCLUDESUPERHIDDEN;
        }
        catch { }
        return flags;
    }

    /// <summary>CLSID (with braces) to hidden flag, from the "Desktop Icon Settings" dialog.</summary>
    private static Dictionary<string, bool> HiddenDesktopIcons()
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel");
            if (k != null)
                foreach (var name in k.GetValueNames())
                    if (k.GetValue(name) is int v) map[name] = v != 0;
        }
        catch { }
        return map;
    }
}
