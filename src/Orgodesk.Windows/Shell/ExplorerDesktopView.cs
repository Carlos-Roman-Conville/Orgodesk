using System.Runtime.InteropServices;

namespace Orgodesk.Windows.Shell;

/// <summary>
/// Reads the items Explorer's own desktop view holds, which is exactly what Windows would show on the desktop.
/// Orgodesk hides that view's list window but the view keeps running, so its item list stays current.
/// Used to drop navigation-pane items (Home, Gallery, Libraries, user folders, drives...) that enumerating
/// the desktop root also returns on some Windows builds (FC-25).
/// </summary>
public static class ExplorerDesktopView
{
    private static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private const int SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1, CSIDL_DESKTOP = 0;
    private const uint SVGIO_ALLVIEW = 2;

    /// <summary>Desktop-absolute parsing names of every item in Explorer's desktop view, or null if it can't be read.</summary>
    public static HashSet<string>? VisibleKeys()
    {
        object? shellWindows = null, dispatch = null;
        try
        {
            var type = Type.GetTypeFromCLSID(CLSID_ShellWindows);
            if (type == null) return null;
            shellWindows = Activator.CreateInstance(type);
            if (shellWindows is not IShellWindows sw) return null;

            object loc = CSIDL_DESKTOP, root = null!;
            if (sw.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH, out dispatch) != Const.S_OK || dispatch is not IServiceProvider sp)
                return null;

            var sid = SID_STopLevelBrowser;
            var iidBrowser = typeof(IShellBrowser).GUID;
            if (sp.QueryService(ref sid, ref iidBrowser, out var pBrowser) != Const.S_OK || pBrowser == IntPtr.Zero) return null;
            var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(pBrowser);
            Marshal.Release(pBrowser);

            if (browser.QueryActiveShellView(out var pView) != Const.S_OK || pView == IntPtr.Zero) return null;
            var view = Marshal.GetObjectForIUnknown(pView);
            Marshal.Release(pView);
            if (view is not IFolderView folderView) return null;

            var iidEnum = typeof(IEnumIDList).GUID;
            if (folderView.Items(SVGIO_ALLVIEW, ref iidEnum, out var pEnum) != Const.S_OK || pEnum == IntPtr.Zero) return null;
            var e = (IEnumIDList)Marshal.GetObjectForIUnknown(pEnum);
            Marshal.Release(pEnum);

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (e.Next(1, out var child, out var fetched) == Const.S_OK && fetched == 1)
            {
                // The desktop view's items are children of the desktop root, so the child PIDL is already absolute.
                var key = Shell32.GetNameFromIDList(child, Const.SIGDN_DESKTOPABSOLUTEPARSING);
                if (key != null) keys.Add(key);
                Shell32.ILFree(child);
            }
            // An empty view usually means Explorer hasn't filled it yet (just after login); don't trust it.
            return keys.Count > 0 ? keys : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (dispatch != null && Marshal.IsComObject(dispatch)) Marshal.ReleaseComObject(dispatch);
            if (shellWindows != null && Marshal.IsComObject(shellWindows)) Marshal.ReleaseComObject(shellWindows);
        }
    }

    // IShellWindows is a dual interface; the four IDispatch slots and the methods before FindWindowSW are placeholders.
    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellWindows
    {
        void _GetTypeInfoCount(); void _GetTypeInfo(); void _GetIDsOfNames(); void _Invoke();
        void _get_Count(); void _Item(); void _NewEnum(); void _Register(); void _RegisterPending(); void _Revoke(); void _OnNavigate(); void _OnActivated();
        [PreserveSig]
        int FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object pvarLoc, [MarshalAs(UnmanagedType.Struct)] ref object pvarLocRoot,
            int swClass, out int phwnd, int swfwOptions, [MarshalAs(UnmanagedType.IDispatch)] out object ppdispOut);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void _GetWindow(); void _ContextSensitiveHelp(); void _InsertMenusSB(); void _SetMenuSB(); void _RemoveMenusSB(); void _SetStatusTextSB();
        void _EnableModelessSB(); void _TranslateAcceleratorSB(); void _BrowseObject(); void _GetViewStateStream(); void _GetControlWindow(); void _SendControlMsg();
        [PreserveSig] int QueryActiveShellView(out IntPtr ppshv);
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        void _GetCurrentViewMode(); void _SetCurrentViewMode(); void _GetFolder(); void _Item(); void _ItemCount();
        [PreserveSig] int Items(uint uFlags, ref Guid riid, out IntPtr ppv);
    }
}
