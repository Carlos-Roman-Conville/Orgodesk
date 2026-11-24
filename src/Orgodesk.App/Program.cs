using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using Orgodesk.App.UI;
using Orgodesk.Core.Licensing;
using Orgodesk.Core.Persistence;
using Orgodesk.Windows.Desktop;

namespace Orgodesk.App;

internal static class Program
{
    private const string MutexName = @"Local\Orgodesk.SingleInstance";
    public const string ExitEventName = @"Local\Orgodesk.Exit";
    /// <summary>Exit event of the program's earlier name, so an old copy can be stopped on install.</summary>
    public const string LegacyExitEventName = @"Local\FencesClone.Exit";



    /// <summary>The tool's name for orgo-commands: its lock, its registry entry and the CLI's <c>orgo orgodesk</c>.</summary>
    public const string CommandsName = Orgodesk.Commands.DeskTool.ToolName;

    /// <summary>
    /// orgo-commands (orgoai/PLAN.md Phase 1, FC-55): one JSON request on standard input, one answer out, no window, no
    /// one-copy mutex, behind the AI-connection gate (crc-appshell kit-1.10). Changes go to layout.json under the shared
    /// lock, which the running app takes for its own saves, and the app is told to show them. <c>--data</c> points it at
    /// another data folder (tests, VM copies); the running app always uses the default one.
    /// </summary>
    /// <summary>
    /// Stage markers for finding where command mode waits (FC-64): when the environment variable ORGODESK_CMD_TRACE names a
    /// file, each stage appends one line (time, process id, stage). Nothing happens when it is not set.
    /// </summary>
    internal static void Trace(string stage)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("ORGODESK_CMD_TRACE") is { Length: > 0 } file)
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} pid {Environment.ProcessId} {stage}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    private static int CommandMode(string[] args)
    {
        Trace("command mode: start");
        string data = LayoutStore.DefaultRoot;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--data") data = Path.GetFullPath(args[i + 1]);
        var license = Crc.AppShell.Commands.CommandLicense.Load(Sales.Commands);
        Trace("licence loaded");
        return CommandMode(data, CommandsName, license);
    }

    /// <summary>Command mode against a data folder and a lock name of the caller's (tests pass their own of both).</summary>
    internal static int CommandMode(string data, string lockName, Crc.AppShell.Commands.CommandLicense license,
        TextReader? input = null, TextWriter? output = null)
    {
        var answer = license.CheckAsync().GetAwaiter().GetResult();   // a due key check may wait up to 5 s
        Trace("licence checked: " + answer.AccessName);
        var store = new LayoutStore(data);
        var tool = new Orgodesk.Commands.DeskTool(new Orgodesk.Commands.SharedLayout(store, lockName), new Orgodesk.Commands.DeskSignals(data), Version())
        {
            WorkArea = () => ScreenInfo.PrimaryWorkArea,
            Screen = () => ScreenInfo.VirtualScreen,
            Desktop = () => ReadDesktop(store),
            KnownFolder = Windows.Shell.KnownFolders.Path,
        };
        Trace("tool ready; serving");
        var code = Orgo.Commands.Stdio.Serve(tool, answer.Refuse, input, output,
            new Orgo.Commands.AiConnection(answer.AccessName, answer.Why, answer.TrialDaysLeft));
        Trace("answered, exit " + code);
        return code;
    }

    /// <summary>What the desktop shows now, as the app reads it (items already placed in a group are kept); null when
    /// the shell cannot be read, so a command never takes "nothing there" for the truth (FC-51).</summary>
    private static IReadOnlyList<Core.Rules.ItemFacts>? ReadDesktop(LayoutStore store)
    {
        try
        {
            var layout = store.PeekLayout(out _);
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (layout != null)
                foreach (var page in layout.Pages.Concat(layout.Presets))
                {
                    placed.UnionWith(page.FreeItems.Keys);
                    foreach (var f in page.Fences.Where(f => !f.IsPortal)) placed.UnionWith(f.Items);
                }
            var items = Windows.Shell.ShellEnumerator.EnumerateDesktop(placed);
            try { return items.Select(i => i.ToFacts()).ToList(); }
            finally { foreach (var i in items) i.Dispose(); }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Error("Could not read the desktop for a command", ex);
            return null;
        }
    }

    /// <summary>
    /// <c>--autostart on|off</c>, for the installer (FC-62): sets the Run-key entry for this exe and the matching
    /// settings.json value in one step, with no window. Both, because at its next start the app makes the Run key follow
    /// settings.json (so a Run key set alone would be undone). Exit code 0 done, 1 could not, 2 not "on" or "off".
    /// </summary>
    private static int SetAutostart(string? value)
    {
        if (value is null || !(value.Equals("on", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase))) return 2;
        bool on = value.Equals("on", StringComparison.OrdinalIgnoreCase);
        try
        {
            LayoutStore.MigrateLegacyRoot();
            var store = new LayoutStore(LayoutStore.DefaultRoot);
            var settings = store.LoadSettings();
            settings.StartWithWindows = on;
            store.SaveSettings(settings);
            AutoStart.SetEnabled(on, Environment.ProcessPath ?? Application.ExecutablePath);
            Log.Info("Start with Windows set " + (on ? "on" : "off") + " by the installer");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Error("Could not set start with Windows", ex);
            return 1;
        }
    }

    /// <summary>The app's version as 1.0.1 (the csproj's Version).</summary>
    private static string Version() => typeof(Program).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    [STAThread]
    private static int Main(string[] args)
    {
        Trace("main");
        if (Orgo.Commands.Stdio.Wanted(args)) return CommandMode(args);
        int autostart = Array.FindIndex(args, x => x.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
        if (autostart >= 0) return SetAutostart(args.ElementAtOrDefault(autostart + 1));
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
            return Installer.Uninstall(quiet: args.Any(a => a.Equals("--quiet", StringComparison.OrdinalIgnoreCase)));

        if (args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase)))
        {
            // Ask the running instance to shut down cleanly (it restores the desktop icons itself).
            if (!EventWaitHandle.TryOpenExisting(ExitEventName, out var ev)) return 1;
            using (ev) ev.Set();
            return 0;
        }

        ApplicationConfiguration.Initialize();   // before any dialog, including the install offer below

        // A buyer who double-clicks the downloaded exe gets offered a proper install (Start Menu, start with Windows,
        // Settings > Apps entry) instead of running from Downloads. Asked before the single-instance lock, because the
        // installer starts the installed copy. Development builds never ask.
        if (!Sales.Config.IsDevBuild && !File.Exists(Installer.InstalledExe)
            && !string.Equals(Path.GetFullPath(Environment.ProcessPath ?? Application.ExecutablePath), Path.GetFullPath(Installer.InstalledExe), StringComparison.OrdinalIgnoreCase))
        {
            var answer = MessageBox.Show(
                "Install Orgodesk on this PC?\n\nYes (recommended): copies it to your user folder, adds it to the Start menu and starts it with Windows. You can remove it any time in Settings > Apps.\nNo: just run this copy now.",
                "Orgodesk", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes) return Installer.Install();
        }

        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            Log.Info("Second copy started while one is running; closing it");
            MessageBox.Show("Orgodesk is already running, so this second copy has closed. Look for its icon at the bottom right of the taskbar (it may be under the ^ arrow) and right-click it for Settings.", "Orgodesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Application.OleRequired(); // shell APIs (SHParseDisplayName, SHChangeNotifyRegister) need COM on this thread
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("Unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Fatal exception", e.ExceptionObject as Exception);
            try { DesktopWindows.SetDesktopIconsVisible(true); TrayContext.ClearHiddenFlag(); } catch { }
        };

        Log.Info($"Started (pid {Environment.ProcessId}){(args.Length > 0 ? " args: " + string.Join(' ', args) : "")}");
        // So the orgo command line (and OrgoAI) can find this exe. A failure only means commands cannot reach Orgodesk.
        try { new Orgo.Commands.ToolRegistry().Register(CommandsName, Environment.ProcessPath!, Version()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Log.Error("Could not register for orgo commands", ex); }
        var store = new LayoutStore(LayoutStore.DefaultRoot);

        // Trial and license gate. Runs before the real desktop icons are hidden, so leaving here is always safe.
        var licenseStore = new LicenseStore(LayoutStore.DefaultRoot, Sales.Config.IsDevBuild ? null : new RegistryTrialMarker());
        var license = licenseStore.LoadOrStart(DateTime.UtcNow);
        var status = LicenseRules.Evaluate(license, Sales.Config, DateTime.UtcNow);
        Log.Info($"License state: {status.State} ({status.Message})");
        if (!status.CanRun)
        {
            // If an earlier Orgodesk was killed or crashed, Explorer's own icons are still hidden. Orgodesk isn't going
            // to draw the desktop now, so give the normal icons back before showing the license window (FC-42).
            try { DesktopWindows.SetDesktopIconsVisible(true); TrayContext.ClearHiddenFlag(); } catch { }
            using var form = new LicenseForm(licenseStore, license, Sales.Config);
            form.ShowDialog();
            if (!form.Status.CanRun) return 3;
        }

        using var context = new TrayContext(store, licenseStore, license);
        if (!context.Start()) return 2;
        Application.Run(context);
        Log.Info("Exited");
        return 0;
    }
}

/// <summary>
/// Keeps the trial start in HKCU\Software\Orgodesk next to the activation hash (M3), so deleting license.json does not
/// start a new trial. Uninstall keeps this key, even when it deletes the data folder.
/// </summary>
internal sealed class RegistryTrialMarker : ITrialMarker
{
    private const string KeyPath = @"Software\Orgodesk";
    private const string ValueName = "TrialStart";

    public DateTime? Read()
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
        return k?.GetValue(ValueName) is string s
            && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
            ? t.ToUniversalTime() : null;
    }

    public void Write(DateTime firstRunUtc)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(KeyPath);
            k?.SetValue(ValueName, DateTime.SpecifyKind(firstRunUtc, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { Log.Error("Could not remember the trial start", ex); }
    }
}
