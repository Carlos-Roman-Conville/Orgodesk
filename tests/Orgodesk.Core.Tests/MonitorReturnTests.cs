using System.Drawing;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Persistence;
using Xunit;

namespace Orgodesk.Core.Tests;

/// <summary>
/// FC-67: a monitor that is unplugged for a moment (an HDMI cable pulled and put back) must not leave its Orgos piled on
/// the other screen. Rex's left 100% monitor went away; its Orgos were pulled to x = 0 on the 150% main screen and stayed.
/// </summary>
public class MonitorReturnTests
{
    // Rex's screens, physical pixels: left 1920x1080 at x -1920, main 2560x1440 at 0.
    private static readonly Rectangle BothScreens = Rectangle.FromLTRB(-1920, 0, 2560, 1440);
    private static readonly Rectangle MainOnly = new(0, 0, 2560, 1440);
    private static readonly Rectangle OnLeft = new(-952, 156, 294, 418);
    private static readonly Rectangle OnMain = new(687, 0, 414, 747);

    private static Layout TwoOrgos()
    {
        var l = new Layout();
        l.ActivePage.Fences.Add(new Fence { Id = "left", Bounds = OnLeft });
        l.ActivePage.Fences.Add(new Fence { Id = "main", Bounds = OnMain });
        return l;
    }

    [Fact]
    public void OrgoGoesBackWhenItsMonitorReturns()
    {
        var m = LayoutMetrics.Default;
        var l = TwoOrgos();
        LayoutReconciler.ClampToArea(l, MainOnly, m);           // unplugged
        Assert.True(MainOnly.Contains(l.FindFence("left")!.Bounds));
        LayoutReconciler.ClampToArea(l, BothScreens, m);        // plugged back in
        Assert.Equal(OnLeft, l.FindFence("left")!.Bounds);
        Assert.Null(l.FindFence("left")!.HomeBounds);
        Assert.Equal(OnMain, l.FindFence("main")!.Bounds);
        Assert.Null(l.FindFence("main")!.HomeBounds);
    }

    [Fact]
    public void AnOrgoMovedByHandWhileAwayStaysWhereItWasPut()
    {
        var m = LayoutMetrics.Default;
        var l = TwoOrgos();
        LayoutReconciler.ClampToArea(l, MainOnly, m);
        var f = l.FindFence("left")!;
        f.MoveByHand(new Rectangle(1500, 600, 294, 418));
        LayoutReconciler.ClampToArea(l, BothScreens, m);
        Assert.Equal(new Rectangle(1500, 600, 294, 418), f.Bounds);
    }

    [Fact]
    public void HomeSurvivesARestartWhileTheMonitorIsAway()
    {
        var root = Path.Combine(Path.GetTempPath(), "orgodesk-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var l = TwoOrgos();
            LayoutReconciler.ClampToArea(l, MainOnly, LayoutMetrics.Default);
            var store = new LayoutStore(root);
            store.SaveLayout(l);
            var back = store.LoadLayout();
            Assert.Equal(OnLeft, back.FindFence("left")!.HomeBounds);
            Assert.Null(back.FindFence("main")!.HomeBounds);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    // Free icons (not in an Orgo) pulled off the left monitor go back the same way.
    private static readonly Point IconOnLeft = new(-1848, 184);

    private static Layout IconOnTheLeft()
    {
        var l = new Layout();
        l.ActivePage.FreeItems["left.lnk"] = IconOnLeft;
        l.ActivePage.FreeItems["main.lnk"] = new Point(0, 0);
        return l;
    }

    [Fact]
    public void FreeIconGoesBackWhenItsMonitorReturns()
    {
        var m = LayoutMetrics.Default;
        var l = IconOnTheLeft();
        LayoutReconciler.ClampToArea(l, MainOnly, m);
        Assert.True(MainOnly.Contains(FreeGrid.CellAt(l.ActivePage.FreeItems["left.lnk"], m)));
        LayoutReconciler.ClampToArea(l, BothScreens, m);
        Assert.Equal(IconOnLeft, l.ActivePage.FreeItems["left.lnk"]);
        Assert.Equal(new Point(0, 0), l.ActivePage.FreeItems["main.lnk"]);
        Assert.Empty(l.ActivePage.FreeHomes);
    }

    [Fact]
    public void AFreeIconMovedByHandWhileAwayStaysWhereItWasPut()
    {
        var m = LayoutMetrics.Default;
        var l = IconOnTheLeft();
        LayoutReconciler.ClampToArea(l, MainOnly, m);
        l.ActivePage.Remove("LEFT.lnk");   // a drag takes the icon out first, then puts it down (any case of the name)
        l.ActivePage.FreeItems["left.lnk"] = new Point(880, 460);
        LayoutReconciler.ClampToArea(l, BothScreens, m);
        Assert.Equal(new Point(880, 460), l.ActivePage.FreeItems["left.lnk"]);
    }

    [Fact]
    public void AFreeIconNeverGoesBackOntoAnother()
    {
        var m = LayoutMetrics.Default;
        var l = IconOnTheLeft();
        LayoutReconciler.ClampToArea(l, MainOnly, m);
        var pulledTo = l.ActivePage.FreeItems["left.lnk"];
        l.ActivePage.FreeItems["new.lnk"] = IconOnLeft;   // something took its old cell meanwhile
        LayoutReconciler.ClampToArea(l, BothScreens, m);
        Assert.Equal(pulledTo, l.ActivePage.FreeItems["left.lnk"]);
    }

    [Fact]
    public void FreeIconHomeSurvivesARestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "orgodesk-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var l = IconOnTheLeft();
            LayoutReconciler.ClampToArea(l, MainOnly, LayoutMetrics.Default);
            var store = new LayoutStore(root);
            store.SaveLayout(l);
            var back = store.LoadLayout();
            Assert.Equal(IconOnLeft, back.ActivePage.FreeHomes["LEFT.LNK"]);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    // FC-71: Rex, 2026-11-25, both HDMI cables out and back in. With no monitor Windows reports a 1920x1080 screen at
    // 100%, so the scale change (Rescale) ran as well as the clamp. Rescale squeezed every Orgo into that screen and kept
    // no home, so the main screen's right-hand Orgos stayed at x 1506 when the 2560 px screen came back.
    private static readonly Rectangle OnMainRight = new(2146, 96, 414, 747);

    // What DesktopSurface does on a display change: rescale to the main screen's scale, then clamp to the whole screen.
    private static void DisplayChange(Layout l, double scale, Rectangle work, Rectangle virtualScreen)
    {
        var m = LayoutMetrics.Default.Scale(scale);
        LayoutReconciler.Rescale(l, scale, work, m);
        LayoutReconciler.ClampToArea(l, virtualScreen, m);
    }

    private static void Near(Rectangle want, Rectangle got) =>
        Assert.True(Math.Abs(want.X - got.X) <= 1 && Math.Abs(want.Y - got.Y) <= 1
            && Math.Abs(want.Width - got.Width) <= 1 && Math.Abs(want.Height - got.Height) <= 1, $"want {want}, got {got}");

    [Fact]
    public void OrgosGoBackAfterBothMonitorsWereUnpluggedAndTheScaleDropped()
    {
        var l = TwoOrgos();
        l.ActivePage.Fences.Add(new Fence { Id = "right", Bounds = OnMainRight });
        l.GridScale = 1.5;
        DisplayChange(l, 1.0, new Rectangle(0, 0, 1920, 1040), new Rectangle(0, 0, 1920, 1080));   // both unplugged
        DisplayChange(l, 1.5, new Rectangle(0, 0, 2560, 1400), BothScreens);                        // both back
        Near(OnLeft, l.FindFence("left")!.Bounds);
        Near(OnMain, l.FindFence("main")!.Bounds);
        Near(OnMainRight, l.FindFence("right")!.Bounds);
    }

    [Fact]
    public void FreeIconGoesBackAfterTheScaleDropped()
    {
        var l = IconOnTheLeft();
        var home = FreeGrid.Snap(IconOnLeft, MainOnly, LayoutMetrics.Default.Scale(1.5), keepInside: false);   // on the 150% grid
        l.ActivePage.FreeItems["left.lnk"] = home;
        l.GridScale = 1.5;
        DisplayChange(l, 1.0, new Rectangle(0, 0, 1920, 1040), new Rectangle(0, 0, 1920, 1080));
        DisplayChange(l, 1.5, new Rectangle(0, 0, 2560, 1400), BothScreens);
        var p = l.ActivePage.FreeItems["left.lnk"];
        Assert.True(Math.Abs(p.X - home.X) <= 1 && Math.Abs(p.Y - home.Y) <= 1, $"want {home}, got {p}");
    }
}
