using System.Windows.Forms;
using Orgodesk.App.UI;
using Orgodesk.Core.Persistence;
using Orgodesk.Windows.Desktop;

namespace Orgodesk.App;

internal static class Program
{
    private const string MutexName = @"Local\Orgodesk.SingleInstance";
    public const string ExitEventName = @"Local\Orgodesk.Exit";
    /// <summary>Exit event of the program's earlier name, so an old copy can be stopped on install.</summary>
    public const string LegacyExitEventName = @"Local\FencesClone.Exit";

    [STAThread]
    private static int Main(string[] args)
    {
        LayoutStore.MigrateLegacyRoot();
        if (args.Any(a => a.Equals("--restore", StringComparison.OrdinalIgnoreCase)))
        {
            // Emergency path: bring the real desktop icons back and leave.
            bool ok = DesktopWindows.SetDesktopIconsVisible(true);
            TrayContext.ClearHiddenFlag();
            return ok ? 0 : 1;
        }

        if (args.Any(a => a.Equals("--diag", StringComparison.OrdinalIgnoreCase)))
        {
            // What this process sees of the desktop windows, for troubleshooting.
            var dv = DesktopWindows.FindDefView();
            var lv = DesktopWindows.FindListView(dv);
            var text = $"Progman={DesktopWindows.FindProgman()} DefView={dv} ListView={lv} " +
                       $"listVisible={(lv != IntPtr.Zero && Windows.Shell.User32.IsWindowVisible(lv))} " +
                       $"virtualScreen={ScreenInfo.VirtualScreen} defViewRect={DesktopWindows.WindowRect(dv)}";
            Log.Info("diag: " + text);
            MessageBox.Show(text, "Orgodesk diagnostics");
            return 0;
        }

        if (args.Any(a => a.Equals("--install", StringComparison.OrdinalIgnoreCase)))
            return Installer.Install();
        if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            return Installer.Uninstall();

        if (args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase)))
        {
            // Ask the running instance to shut down cleanly (it restores the desktop icons itself).
            if (!EventWaitHandle.TryOpenExisting(ExitEventName, out var ev)) return 1;
            using (ev) ev.Set();
            return 0;
        }

        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            Log.Info("Second copy started while one is running; closing it");
            MessageBox.Show("Orgodesk is already running. Use its tray icon.", "Orgodesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.OleRequired(); // shell APIs (SHParseDisplayName, SHChangeNotifyRegister) need COM on this thread
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("Unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Fatal exception", e.ExceptionObject as Exception);
            try { DesktopWindows.SetDesktopIconsVisible(true); TrayContext.ClearHiddenFlag(); } catch { }
        };

        Log.Info($"Started (pid {Environment.ProcessId}){(args.Length > 0 ? " args: " + string.Join(' ', args) : "")}");
        var store = new LayoutStore(LayoutStore.DefaultRoot);
        using var context = new TrayContext(store);
        if (!context.Start()) return 2;
        Application.Run(context);
        Log.Info("Exited");
        return 0;
    }
}
