using System.Drawing;
using Orgodesk.Windows.Shell;

namespace Orgodesk.Windows.Desktop;

/// <summary>Finds the Explorer desktop windows (Progman, WorkerW, SHELLDLL_DefView, SysListView32) and toggles the real icons.</summary>
public static class DesktopWindows
{
    private const int ToggleDesktopIconsCommand = 0x7402;

    public static IntPtr FindProgman() => User32.FindWindow("Progman", null);

    /// <summary>
    /// The SHELLDLL_DefView that hosts the desktop icons. On most builds it is under Progman;
    /// on others it lives inside a top-level WorkerW (or a WorkerW child of Progman on 24H2+).
    /// </summary>
    public static IntPtr FindDefView()
    {
        var progman = FindProgman();
        if (progman != IntPtr.Zero)
        {
            var dv = FindDescendant(progman, "SHELLDLL_DefView");
            if (dv != IntPtr.Zero) return dv;
        }
        IntPtr found = IntPtr.Zero;
        User32.EnumWindows((hwnd, _) =>
        {
            if (User32.GetClassName(hwnd) != "WorkerW") return true;
            var dv = FindDescendant(hwnd, "SHELLDLL_DefView");
            if (dv == IntPtr.Zero) return true;
            found = dv;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FindListView(IntPtr defView)
        => defView == IntPtr.Zero ? IntPtr.Zero : User32.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);

    private static IntPtr FindDescendant(IntPtr parent, string className)
    {
        IntPtr found = IntPtr.Zero;
        User32.EnumChildWindows(parent, (hwnd, _) =>
        {
            if (User32.GetClassName(hwnd) != className) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static bool AreDesktopIconsVisible()
    {
        var lv = FindListView(FindDefView());
        return lv != IntPtr.Zero && User32.IsWindowVisible(lv);
    }

    /// <summary>
    /// Shows or hides the real desktop icon list. The list window is shown/hidden directly (instant, nothing
    /// persisted, so a crash cannot leave the desktop empty after a reboot). The desktop's own toggle command
    /// is only a fallback: it flips a registry value and Explorer can take many seconds to apply it.
    /// </summary>
    /// <summary>
    /// Re-enables Explorer's desktop window if it was left disabled (by a dialog of an earlier Orgodesk that was closed
    /// or killed while the dialog was open; FC-38). Returns true if it had to be repaired.
    /// </summary>
    public static bool RepairDisabledDesktop()
    {
        bool repaired = false;
        foreach (var h in new[] { FindProgman(), FindDefView() })
        {
            if (h == IntPtr.Zero || User32.IsWindowEnabled(h)) continue;
            User32.EnableWindow(h, true);
            repaired = true;
        }
        return repaired;
    }

    public static bool SetDesktopIconsVisible(bool visible)
    {
        var defView = FindDefView();
        if (defView == IntPtr.Zero) return false;
        var lv = FindListView(defView);
        if (lv == IntPtr.Zero) return false;
        if (User32.IsWindowVisible(lv) == visible) return true;

        User32.ShowWindow(lv, visible ? SW_SHOWNA : SW_HIDE);
        for (int i = 0; i < 10; i++)
        {
            if (User32.IsWindowVisible(lv) == visible) return true;
            Thread.Sleep(50);
        }

        User32.SendMessageTimeout(defView, Const.WM_COMMAND, (IntPtr)ToggleDesktopIconsCommand, IntPtr.Zero,
            Const.SMTO_ABORTIFHUNG, 2000, out _);
        for (int i = 0; i < 200; i++)
        {
            if (User32.IsWindowVisible(lv) == visible) return true;
            Thread.Sleep(50);
        }
        return User32.IsWindowVisible(lv) == visible;
    }

    private const int SW_HIDE = 0;
    private const int SW_SHOWNA = 8;

    public static uint ThreadOf(IntPtr hwnd) => User32.GetWindowThreadProcessId(hwnd, out _);

    public static Rectangle WindowRect(IntPtr hwnd)
        => User32.GetWindowRect(hwnd, out var r) ? r.ToRectangle() : Rectangle.Empty;

    public static Rectangle ClientRect(IntPtr hwnd)
        => User32.GetClientRect(hwnd, out var r) ? r.ToRectangle() : Rectangle.Empty;
}
