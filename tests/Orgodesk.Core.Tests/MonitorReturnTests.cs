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
}
