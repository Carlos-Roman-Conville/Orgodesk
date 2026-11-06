using System.Drawing;
using System.Runtime.InteropServices;
using Orgodesk.Windows.Shell;

namespace Orgodesk.Windows.Desktop;

/// <summary>
/// A low-level mouse hook that reports a double right-click on the desktop (Explorer's desktop or this app's
/// surface, which is a child of it). Works whether or not the surface is showing, so it can both suspend and
/// resume the app. The callback does almost nothing; the event is raised on the hook's thread.
/// </summary>
public sealed class DesktopMouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_RBUTTONUP = 0x0205;
    private const uint GA_ROOT = 2;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int n);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);

    private readonly HookProc _proc; // kept alive for the lifetime of the hook
    private IntPtr _hook;
    private uint _lastUpTime;
    private POINT _lastUpPoint;
    private bool _lastOnDesktop;

    /// <summary>Raised (on the hook thread) after two right-button releases on the desktop within the double-click time.</summary>
    public event Action<Point>? RightDoubleClick;

    public DesktopMouseHook()
    {
        _proc = Callback;
        _hook = SetWindowsHookExW(WH_MOUSE_LL, _proc, GetModuleHandleW(null), 0);
        if (_hook == IntPtr.Zero) throw new InvalidOperationException("SetWindowsHookEx failed: " + Marshal.GetLastWin32Error());
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_RBUTTONUP)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            uint now = info.time;
            bool quick = now - _lastUpTime <= GetDoubleClickTime();
            bool close = Math.Abs(info.pt.X - _lastUpPoint.X) <= GetSystemMetrics(36) && Math.Abs(info.pt.Y - _lastUpPoint.Y) <= GetSystemMetrics(37);
            // The first right-click opens a context menu under the cursor (a classic #32768 popup on the clone,
            // a XAML popup on the Windows 11 desktop), so the second click lands on that menu. A quick second
            // right-click at the same spot after a desktop right-click is unambiguous: count it whatever is under it.
            if (quick && close && _lastOnDesktop)
            {
                _lastUpTime = 0;
                _lastOnDesktop = false;
                try { RightDoubleClick?.Invoke(info.pt.ToPoint()); } catch { }
            }
            else
            {
                _lastUpTime = now;
                _lastUpPoint = info.pt;
                _lastOnDesktop = IsDesktop(info.pt);
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsDesktop(POINT pt)
    {
        var hwnd = WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return false;
        var root = User32.GetAncestor(hwnd, GA_ROOT);
        var cls = User32.GetClassName(root);
        return cls == "Progman" || cls == "WorkerW";
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }
}
