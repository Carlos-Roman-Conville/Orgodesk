using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Orgodesk.Windows.Shell;

/// <summary>Open, rename, move, delete and drag shell items; build their context menus.</summary>
public static class ShellActions
{
    /// <summary>Opens the item with its default verb (what a double-click does).</summary>
    public static bool Open(ShellItemInfo item, IntPtr hwnd)
    {
        var sei = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = Const.SEE_MASK_IDLIST,
            hwnd = hwnd,
            lpIDList = item.AbsolutePidl,
            nShow = Const.SW_SHOWNORMAL,
        };
        return Shell32.ShellExecuteEx(ref sei);
    }

    public static bool OpenPath(string path, IntPtr hwnd)
    {
        var sei = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            hwnd = hwnd,
            lpFile = path,
            nShow = Const.SW_SHOWNORMAL,
        };
        return Shell32.ShellExecuteEx(ref sei);
    }

    /// <summary>The shell context menu for one or more items that share a parent folder.</summary>
    public static ContextMenuSession CreateItemMenu(IReadOnlyList<ShellItemInfo> items, IntPtr hwnd)
    {
        if (items.Count == 0) throw new ArgumentException("No items", nameof(items));
        var parent = items[0].Parent;
        var pidls = items.Select(i => i.ChildPidl).ToArray();
        var iid = Iids.IContextMenu;
        parent.GetUIObjectOf(hwnd, (uint)pidls.Length, pidls, ref iid, IntPtr.Zero, out var ppv);
        var cm = Marshal.GetObjectForIUnknown(ppv);
        Marshal.Release(ppv);
        return new ContextMenuSession(cm);
    }

    /// <summary>The background context menu of a folder (what right-clicking empty desktop shows).</summary>
    public static ContextMenuSession CreateBackgroundMenu(IShellFolder folder, IntPtr hwnd)
    {
        var iid = Iids.IContextMenu;
        folder.CreateViewObject(hwnd, ref iid, out var ppv);
        var cm = Marshal.GetObjectForIUnknown(ppv);
        Marshal.Release(ppv);
        return new ContextMenuSession(cm);
    }

    /// <summary>An OLE data object for dragging the items to other windows.</summary>
    public static ComIDataObject? CreateDataObject(IReadOnlyList<ShellItemInfo> items, IntPtr hwnd)
    {
        if (items.Count == 0) return null;
        var parent = items[0].Parent;
        var pidls = items.Select(i => i.ChildPidl).ToArray();
        var iid = Iids.IDataObject;
        try
        {
            parent.GetUIObjectOf(hwnd, (uint)pidls.Length, pidls, ref iid, IntPtr.Zero, out var ppv);
            var obj = (ComIDataObject)Marshal.GetObjectForIUnknown(ppv);
            Marshal.Release(ppv);
            return obj;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Renames an item. Keeps a hidden extension if the display name did not show one.</summary>
    public static bool Rename(ShellItemInfo item, string newDisplayName, IntPtr hwnd)
    {
        newDisplayName = newDisplayName.Trim();
        if (newDisplayName.Length == 0 || newDisplayName == item.DisplayName) return false;
        if (item.Path != null)
        {
            var fileName = Path.GetFileName(item.Path);
            var ext = Path.GetExtension(item.Path);
            bool extHidden = !item.IsFolder && ext.Length > 0
                && !fileName.Equals(item.DisplayName, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(fileName).Equals(item.DisplayName, StringComparison.OrdinalIgnoreCase);
            var newName = extHidden && !newDisplayName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? newDisplayName + ext : newDisplayName;
            var dir = Path.GetDirectoryName(item.Path) ?? "";
            var dest = Path.Combine(dir, newName);
            var op = new SHFILEOPSTRUCT
            {
                hwnd = hwnd,
                wFunc = Const.FO_RENAME,
                pFrom = item.Path + "\0\0",
                pTo = dest + "\0\0",
                fFlags = Const.FOF_ALLOWUNDO,
            };
            return Shell32.SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
        }
        try
        {
            item.Parent.SetNameOf(hwnd, item.ChildPidl, newDisplayName, Const.SIGDN_NORMALDISPLAY, out var newPidl);
            if (newPidl != IntPtr.Zero) Shell32.ILFree(newPidl);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Moves or copies files into a folder with the normal shell UI (progress, conflicts, undo).</summary>
    public static bool Transfer(IEnumerable<string> paths, string destFolder, bool copy, IntPtr hwnd)
    {
        var list = paths.Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (list.Count == 0) return false;
        var op = new SHFILEOPSTRUCT
        {
            hwnd = hwnd,
            wFunc = copy ? Const.FO_COPY : Const.FO_MOVE,
            pFrom = string.Join("\0", list) + "\0\0",
            pTo = destFolder + "\0\0",
            fFlags = Const.FOF_ALLOWUNDO | Const.FOF_NOCONFIRMMKDIR,
        };
        return Shell32.SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    /// <summary>Sends files to the Recycle Bin (or deletes permanently) with the normal shell confirmation.</summary>
    public static bool Delete(IEnumerable<string> paths, IntPtr hwnd, bool permanent = false)
    {
        var list = paths.Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (list.Count == 0) return false;
        var op = new SHFILEOPSTRUCT
        {
            hwnd = hwnd,
            wFunc = Const.FO_DELETE,
            pFrom = string.Join("\0", list) + "\0\0",
            fFlags = permanent ? (ushort)0 : Const.FOF_ALLOWUNDO,
        };
        return Shell32.SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    /// <summary>Invokes a named verb ("properties", "delete", "open") on items through their context menu.</summary>
    public static bool InvokeVerb(IReadOnlyList<ShellItemInfo> items, string verb, IntPtr hwnd)
    {
        if (items.Count == 0) return false;
        using var menu = CreateItemMenu(items, hwnd);
        return menu.InvokeVerb(verb, hwnd);
    }

    public static bool IsSameVolume(string a, string b)
    {
        try { return string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}

/// <summary>
/// One popup of a shell context menu. Create, add your own items, Show, then Invoke or act on the returned id.
/// Forward WM_INITMENUPOPUP / WM_DRAWITEM / WM_MEASUREITEM / WM_MENUCHAR to <see cref="HandleMenuMessage"/> while shown.
/// </summary>
public sealed class ContextMenuSession : IDisposable
{
    public const uint ShellIdFirst = 0x1000;
    public const uint ShellIdLast = 0x7FFF;

    private readonly IContextMenu _cm;
    private readonly IContextMenu2? _cm2;
    private readonly IContextMenu3? _cm3;
    private readonly List<IntPtr> _subMenus = new();
    private IntPtr _hMenu;
    private bool _populated;

    public IntPtr Menu => _hMenu;

    public ContextMenuSession(object comMenu)
    {
        _cm = (IContextMenu)comMenu;
        _cm2 = comMenu as IContextMenu2;
        _cm3 = comMenu as IContextMenu3;
        _hMenu = User32.CreatePopupMenu();
    }

    /// <summary>Fills the menu with the shell's items.</summary>
    public void Populate(uint flags = Const.CMF_NORMAL | Const.CMF_EXPLORE | Const.CMF_CANRENAME)
    {
        if (_populated) return;
        if ((User32.GetKeyState(0x10) & 0x8000) != 0) flags |= Const.CMF_EXTENDEDVERBS; // Shift held
        _cm.QueryContextMenu(_hMenu, 0, ShellIdFirst, ShellIdLast, flags);
        _populated = true;
    }

    /// <summary>Inserts one of our own items (id must be below <see cref="ShellIdFirst"/>) at a position; -1 appends.</summary>
    public void AddOwnItem(uint id, string text, int position = -1, bool enabled = true, bool isChecked = false)
    {
        uint flags = Const.MF_STRING | (enabled ? 0 : Const.MF_GRAYED) | (isChecked ? Const.MF_CHECKED : 0);
        if (position < 0) User32.AppendMenu(_hMenu, flags, (UIntPtr)id, text);
        else User32.InsertMenu(_hMenu, (uint)position, flags | Const.MF_BYPOSITION, (UIntPtr)id, text);
    }

    public void AddSeparator(int position = -1)
    {
        if (position < 0) User32.AppendMenu(_hMenu, Const.MF_SEPARATOR, UIntPtr.Zero, null);
        else User32.InsertMenu(_hMenu, (uint)position, Const.MF_SEPARATOR | Const.MF_BYPOSITION, UIntPtr.Zero, null);
    }

    /// <summary>Adds a submenu of our own; returns a handle to append items to with <see cref="AddSubItem"/>.</summary>
    public IntPtr AddOwnSubmenu(string text, int position = -1)
    {
        var sub = User32.CreatePopupMenu();
        _subMenus.Add(sub);
        if (position < 0) User32.AppendMenu(_hMenu, Const.MF_POPUP | Const.MF_STRING, (UIntPtr)(ulong)sub, text);
        else User32.InsertMenu(_hMenu, (uint)position, Const.MF_POPUP | Const.MF_STRING | Const.MF_BYPOSITION, (UIntPtr)(ulong)sub, text);
        return sub;
    }

    public static void AddSubItem(IntPtr submenu, uint id, string text, bool isChecked = false, bool enabled = true)
    {
        uint flags = Const.MF_STRING | (isChecked ? Const.MF_CHECKED : 0) | (enabled ? 0 : Const.MF_GRAYED);
        User32.AppendMenu(submenu, flags, (UIntPtr)id, text);
    }

    public static void AddSubSeparator(IntPtr submenu) => User32.AppendMenu(submenu, Const.MF_SEPARATOR, UIntPtr.Zero, null);

    public int ItemCount => User32.GetMenuItemCount(_hMenu);

    /// <summary>Shows the menu and returns the chosen command id, or 0.</summary>
    public uint Show(IntPtr hwnd, Point screenPoint)
    {
        User32.SetForegroundWindow(hwnd);
        return User32.TrackPopupMenuEx(_hMenu, Const.TPM_RETURNCMD | Const.TPM_RIGHTBUTTON | Const.TPM_LEFTALIGN,
            screenPoint.X, screenPoint.Y, hwnd, IntPtr.Zero);
    }

    public static bool IsShellCommand(uint id) => id >= ShellIdFirst && id <= ShellIdLast;

    /// <summary>The verb name ("rename", "delete", ...) of a shell command id, or null.</summary>
    public string? GetVerb(uint id)
    {
        if (!IsShellCommand(id)) return null;
        var buf = Marshal.AllocCoTaskMem(1024);
        try
        {
            int hr = _cm.GetCommandString((UIntPtr)(id - ShellIdFirst), Const.GCS_VERBW, IntPtr.Zero, buf, 512);
            return hr == 0 ? Marshal.PtrToStringUni(buf) : null;
        }
        catch { return null; }
        finally { Marshal.FreeCoTaskMem(buf); }
    }

    /// <summary>Runs a shell command chosen from the menu.</summary>
    public bool Invoke(uint id, IntPtr hwnd, Point screenPoint)
    {
        if (!IsShellCommand(id)) return false;
        var info = new CMINVOKECOMMANDINFOEX
        {
            cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
            fMask = Const.CMIC_MASK_UNICODE | Const.CMIC_MASK_PTINVOKE,
            hwnd = hwnd,
            lpVerb = (IntPtr)(id - ShellIdFirst),
            lpVerbW = (IntPtr)(id - ShellIdFirst),
            nShow = Const.SW_SHOWNORMAL,
            ptInvoke = new POINT(screenPoint.X, screenPoint.Y),
        };
        if ((User32.GetKeyState(0x10) & 0x8000) != 0) info.fMask |= Const.CMIC_MASK_SHIFT_DOWN;
        if ((User32.GetKeyState(0x11) & 0x8000) != 0) info.fMask |= Const.CMIC_MASK_CONTROL_DOWN;
        return _cm.InvokeCommand(ref info) == 0;
    }

    /// <summary>Runs a verb by name without showing the menu.</summary>
    public bool InvokeVerb(string verb, IntPtr hwnd)
    {
        Populate();
        var verbA = Marshal.StringToHGlobalAnsi(verb);
        var verbW = Marshal.StringToHGlobalUni(verb);
        try
        {
            var info = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = Const.CMIC_MASK_UNICODE,
                hwnd = hwnd,
                lpVerb = verbA,
                lpVerbW = verbW,
                nShow = Const.SW_SHOWNORMAL,
            };
            return _cm.InvokeCommand(ref info) == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(verbA);
            Marshal.FreeHGlobal(verbW);
        }
    }

    /// <summary>Lets the shell draw owner-drawn submenus (Open with, Send to). Returns true if handled.</summary>
    public bool HandleMenuMessage(ref Message m)
    {
        switch (m.Msg)
        {
            case Const.WM_INITMENUPOPUP:
            case Const.WM_DRAWITEM:
            case Const.WM_MEASUREITEM:
            case Const.WM_MENUCHAR:
                if (_cm3 != null)
                {
                    if (_cm3.HandleMenuMsg2((uint)m.Msg, m.WParam, m.LParam, out var result) == 0)
                    {
                        m.Result = result;
                        return true;
                    }
                }
                else if (_cm2 != null && _cm2.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam) == 0)
                {
                    m.Result = IntPtr.Zero;
                    return true;
                }
                return false;
        }
        return false;
    }

    public void Dispose()
    {
        foreach (var s in _subMenus) User32.DestroyMenu(s);
        _subMenus.Clear();
        if (_hMenu != IntPtr.Zero) { User32.DestroyMenu(_hMenu); _hMenu = IntPtr.Zero; }
        try { Marshal.FinalReleaseComObject(_cm); } catch { }
    }
}
