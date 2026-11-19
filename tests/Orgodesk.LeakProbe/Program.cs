// FC-44 leak probe: replays hours of Orgodesk's background work in seconds and says whether anything really grows.
// Read-only: it looks at Explorer's desktop windows and lists the desktop; it never hides icons or writes a file.
// Run: dotnet run --project tests/Orgodesk.LeakProbe -c Release      (exit code 1 = a leak was found)
//
// Each path runs in two equal halves. Garbage the collector has not picked up yet is not a leak, so every
// measurement is taken after a forced full collection. A real leak grows by the same amount in both halves.
using System.Diagnostics;
using System.Runtime.InteropServices;
using Orgodesk.Windows.Desktop;
using Orgodesk.Windows.Shell;

internal static class Program
{
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr hProcess, int flags);

    // Native = private bytes minus what the .NET collector has reserved for itself (it keeps freed room for reuse).
    private record Sample(long Managed, long Native, int Handles, int Gdi, int User);

    private static Sample Measure()
    {
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        using var p = Process.GetCurrentProcess();
        return new Sample(GC.GetTotalMemory(true), p.PrivateMemorySize64 - GC.GetGCMemoryInfo().TotalCommittedBytes, p.HandleCount,
            GetGuiResources(p.Handle, 0), GetGuiResources(p.Handle, 1));
    }

    private static bool _failed;

    /// <summary>Runs <paramref name="step"/> 2 x <paramref name="half"/> times; fails if the second half still grows.</summary>
    private static void Check(string name, int half, string equals, Action step)
    {
        for (int i = 0; i < Math.Max(20, half / 10); i++) step();   // warm-up: JIT, caches, first-use allocations
        var a = Measure();
        long rawBefore = GC.GetTotalMemory(false);
        for (int i = 0; i < half; i++) step();
        long garbage = GC.GetTotalMemory(false) - rawBefore;
        var b = Measure();
        for (int i = 0; i < half; i++) step();
        var c = Measure();

        // Limits for the second half: well under 1 byte of managed heap and 64 bytes of native memory per run would
        // already be visible over a week of uptime, so anything above these is called a leak.
        long managed = c.Managed - b.Managed, priv = c.Native - b.Native;
        int handles = c.Handles - b.Handles, gdi = c.Gdi - b.Gdi, user = c.User - b.User;
        bool leak = managed > half * 8L + 65536 || priv > half * 64L + 1024 * 1024 || handles > 8 || gdi > 4 || user > 4;
        _failed |= leak;

        Console.WriteLine($"{name}: {half * 2:N0} runs ({equals})");
        Console.WriteLine($"  throwaway memory per run (collected later, not a leak): {Math.Max(0, garbage) / (double)half:N0} bytes");
        Console.WriteLine($"  after cleanup, 1st half -> 2nd half growth:");
        Console.WriteLine($"    managed  {b.Managed - a.Managed,10:N0} -> {managed,10:N0} bytes");
        Console.WriteLine($"    native   {b.Native - a.Native,10:N0} -> {priv,10:N0} bytes");
        Console.WriteLine($"    handles  {b.Handles - a.Handles,10} -> {handles,10}");
        Console.WriteLine($"    GDI/USER {b.Gdi - a.Gdi,6}/{b.User - a.User,-3} -> {gdi,6}/{user,-3}");
        Console.WriteLine($"  {(leak ? "LEAK" : "flat")}");
    }

    [STAThread]
    private static int Main(string[] args)
    {
        System.Windows.Forms.Application.OleRequired();   // shell calls need COM on this thread (FC-2)
        int scale = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 1;

        // 1. What the 2-second watchdog does while Orgodesk sits idle (DesktopSurface.Watchdog).
        Check("Idle watchdog", 10_000 * scale, $"{20_000 * scale * 2 / 3600.0:N0} hours idle", () =>
        {
            DesktopWindows.AreDesktopIconsVisible();
            _ = ScreenInfo.VirtualScreen;
        });

        // 2. What one desktop change does (DesktopModel.RefreshDesktop + icons + PruneIcons).
        using var icons = new IconProvider();
        Check("Desktop refresh", 150 * scale, "one per file created, deleted or renamed", () =>
        {
            var items = ShellEnumerator.EnumerateDesktop();
            foreach (var it in items) icons.GetIcon(it, 48);
            icons.Prune(items.Select(i => i.Key));
            foreach (var it in items) it.Dispose();
        });

        // 3. Icon cache thrown away and reloaded (a program install or icon change: SHCNE_UPDATEIMAGE).
        Check("Icon reload", 15 * scale, "one per icon-cache reset", () =>
        {
            var items = ShellEnumerator.EnumerateDesktop();
            icons.Clear();
            foreach (var it in items) icons.GetIcon(it, 48);
            foreach (var it in items) it.Dispose();
        });

        Console.WriteLine(_failed ? "RESULT: LEAK FOUND" : "RESULT: no leak; growth seen in a soak is uncollected garbage");
        return _failed ? 1 : 0;
    }
}
