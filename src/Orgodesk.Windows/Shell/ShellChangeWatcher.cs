using System.Windows.Forms;

namespace Orgodesk.Windows.Shell;

/// <summary>
/// Receives shell change notifications (create, delete, rename, icon updates) for the desktop and any
/// watched folders, through a message-only window. Raise-only; the consumer debounces.
/// </summary>
public sealed class ShellChangeWatcher : NativeWindow, IDisposable
{
    private const int WM_SHNOTIFY = Const.WM_USER + 0x101;
    private readonly List<string> _extraFolders = new();
    private readonly List<uint> _registrations = new();
    private bool _disposed;

    /// <summary>Fired on the UI thread with the SHCNE_* event code.</summary>
    public event Action<int>? Changed;

    public ShellChangeWatcher()
    {
        CreateHandle(new CreateParams { Parent = Const.HWND_MESSAGE, Caption = "Orgodesk.ShellWatcher" });
        Register();
    }

    /// <summary>Adds a folder (portal) to watch. Re-registers.</summary>
    public void WatchFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || _extraFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        _extraFolders.Add(path);
        Register();
    }

    public void SetWatchedFolders(IEnumerable<string> paths)
    {
        var wanted = paths.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == _extraFolders.Count && wanted.All(w => _extraFolders.Contains(w, StringComparer.OrdinalIgnoreCase))) return;
        _extraFolders.Clear();
        _extraFolders.AddRange(wanted);
        Register();
    }

    private void Register()
    {
        Unregister();
        // One SHChangeNotifyRegister call per folder: passing several entries in one call
        // faults inside shell32 on this platform (see ISSUES.md).
        var pidls = new List<IntPtr>();
        void Add(IntPtr pidl) { if (pidl != IntPtr.Zero) pidls.Add(pidl); }
        Add(ShellEnumerator.DesktopRootPidl());
        Add(ShellEnumerator.ParsePidl(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
        Add(ShellEnumerator.ParsePidl(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)));
        foreach (var f in _extraFolders) Add(ShellEnumerator.ParsePidl(f));
        try
        {
            foreach (var pidl in pidls)
            {
                var entry = new[] { new SHChangeNotifyEntry { pidl = pidl, fRecursive = 0 } };
                uint id = Shell32.SHChangeNotifyRegister(Handle,
                    Const.SHCNRF_ShellLevel | Const.SHCNRF_InterruptLevel | Const.SHCNRF_NewDelivery,
                    Const.SHCNE_ALLEVENTS, WM_SHNOTIFY, 1, entry);
                if (id != 0) _registrations.Add(id);
            }
        }
        finally
        {
            foreach (var p in pidls) Shell32.ILFree(p);
        }
    }

    private void Unregister()
    {
        foreach (var id in _registrations) Shell32.SHChangeNotifyDeregister(id);
        _registrations.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_SHNOTIFY)
        {
            int evt = 0;
            var hLock = Shell32.SHChangeNotification_Lock(m.WParam, (uint)m.LParam.ToInt64(), out _, out evt);
            if (hLock != IntPtr.Zero) Shell32.SHChangeNotification_Unlock(hLock);
            try { Changed?.Invoke(evt); } catch { /* never let a handler kill the message loop */ }
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
        DestroyHandle();
    }
}
