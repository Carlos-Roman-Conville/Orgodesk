using System.Drawing;
using System.Windows.Forms;
using Orgodesk.Windows.Shell;

namespace Orgodesk.Windows.Desktop;

/// <summary>Virtual screen geometry and per-monitor DPI, in physical pixels.</summary>
public static class ScreenInfo
{
    public static Rectangle VirtualScreen => SystemInformation.VirtualScreen;

    /// <summary>The DPI scale (1.0 = 96 dpi) of the monitor under a screen point.</summary>
    public static double ScaleAt(Point screenPoint)
    {
        var mon = User32.MonitorFromPoint(new POINT(screenPoint.X, screenPoint.Y), Const.MONITOR_DEFAULTTONEAREST);
        if (mon != IntPtr.Zero && Shcore.GetDpiForMonitor(mon, Const.MDT_EFFECTIVE_DPI, out var dx, out _) == 0 && dx > 0)
            return dx / 96.0;
        return User32.GetDpiForSystem() / 96.0;
    }

    /// <summary>The monitor work areas (taskbar excluded), in virtual-screen pixels.</summary>
    public static IReadOnlyList<Rectangle> WorkAreas() => Screen.AllScreens.Select(s => s.WorkingArea).ToList();

    public static Rectangle WorkAreaAt(Point screenPoint) => Screen.FromPoint(screenPoint).WorkingArea;

    public static Rectangle PrimaryWorkArea => Screen.PrimaryScreen?.WorkingArea ?? VirtualScreen;
}
