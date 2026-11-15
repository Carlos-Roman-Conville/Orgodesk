using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;
using Orgodesk.Core.Licensing;
using Orgodesk.Core.Persistence;
using Orgodesk.Core.Updates;
using Orgodesk.Windows.Desktop;

namespace Orgodesk.App.UI;

/// <summary>Owns the tray icon, the surface, the store and the dialogs. Lives for the whole run.</summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly LayoutStore _store;
    private readonly LicenseStore _licenseStore;
    private readonly LicenseRecord _license;
    private readonly DesktopModel _model;
    private readonly DesktopSurface _surface;
    private readonly NotifyIcon _tray;
    private readonly Icon _icon;
    private SettingsForm? _settingsForm;
    private DesktopMouseHook? _mouseHook;
    private EventWaitHandle? _exitEvent;
    private RegisteredWaitHandle? _exitWait;
    private bool _exiting;
    private UpdateInfo? _update;
    private bool _updateBalloonUp;
    /// <summary>Daily: licence check (always) and update check (when UpdateUrl is set).</summary>
    private System.Windows.Forms.Timer? _dailyTimer;
    private bool _licenseCheckBusy;

    private static string HiddenFlagPath => Path.Combine(LayoutStore.DefaultRoot, "icons-hidden.flag");
    public static void ClearHiddenFlag() { try { File.Delete(HiddenFlagPath); } catch { } }

    public TrayContext(LayoutStore store, LicenseStore licenseStore, LicenseRecord license)
    {
        _store = store;
        _licenseStore = licenseStore;
        _license = license;
        _model = new DesktopModel(store.LoadLayout(), store.LoadSettings());
        _surface = new DesktopSurface(_model, store);
        _surface.ExitRequested += (_, _) => Exit();
        _surface.SettingsRequested += (_, _) => ShowSettings();
        _surface.SnapshotsRequested += (_, _) => ShowSnapshots();
        _surface.LayoutChanged += (_, _) => _settingsForm?.RefreshFenceList();

        _icon = MakeTrayIcon();
        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = "Orgodesk",
            Visible = false,
            ContextMenuStrip = BuildTrayMenu(),
        };
        _tray.DoubleClick += (_, _) => ShowSettings();
        _tray.BalloonTipClicked += (_, _) => { if (_updateBalloonUp) OpenUpdatePage(); };
        _tray.BalloonTipClosed += (_, _) => _updateBalloonUp = false;
    }

    /// <summary>Hides the real desktop icons and attaches the surface. False if the desktop could not be found.</summary>
    public bool Start()
    {
        if (!_surface.Attach())
        {
            Log.Error("Desktop window not found; not starting");
            MessageBox.Show("Orgodesk could not find the Windows desktop, so it did not start. Windows Explorer may have stopped.\n\nPress Ctrl+Shift+Esc, choose Run new task, type explorer.exe, press Enter, then start Orgodesk again.",
                "Orgodesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        try { File.WriteAllText(HiddenFlagPath, DateTime.Now.ToString("o")); } catch { }
        if (!DesktopWindows.SetDesktopIconsVisible(false))
            Log.Error("Could not hide the real desktop icons");
        _surface.RefreshDesktop();
        if (_model.Settings.StartWithWindows != AutoStart.IsEnabled())
            AutoStart.SetEnabled(_model.Settings.StartWithWindows, Application.ExecutablePath);
        _tray.Visible = true;
        try
        {
            _mouseHook = new DesktopMouseHook();
            _mouseHook.RightDoubleClick += _ =>
            {
                if (!_model.Settings.ToggleOnDoubleRightClick) return;
                try { _surface.BeginInvoke(new Action(_surface.ToggleSuspend)); } catch { }
            };
        }
        catch (Exception ex)
        {
            Log.Error("Mouse hook failed; double right-click toggle unavailable", ex);
        }
        _exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, Program.ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent, (_, _) =>
        {
            try { _surface.BeginInvoke(new Action(Exit)); } catch { }
        }, null, -1, executeOnlyOnce: true);
        var lic = LicenseRules.Evaluate(_license, Sales.Config, DateTime.UtcNow);
        if (lic.State == LicenseState.Trial && lic.TrialDaysLeft <= 3)
            _tray.ShowBalloonTip(8000, "Orgodesk", $"{lic.Message}. Right-click the tray icon and choose License to buy a key.", ToolTipIcon.Info);
        _ = CheckLicenseWhileRunningAsync();
        // M2: a PC that only ever sleeps never restarts Orgodesk, so the licence is checked again daily and on resume.
        _dailyTimer = new System.Windows.Forms.Timer { Interval = 24 * 60 * 60 * 1000 };
        _dailyTimer.Tick += (_, _) =>
        {
            _ = CheckLicenseWhileRunningAsync();
            if (Sales.UpdateUrl.Length > 0) _ = CheckForUpdateAsync(byHand: false);
        };
        _dailyTimer.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        if (Sales.UpdateUrl.Length > 0) _ = CheckForUpdateAsync(byHand: false);
        return true;
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            var lic = LicenseRules.Evaluate(_license, Sales.Config, DateTime.UtcNow);
            if (lic.State == LicenseState.Trial)
            {
                menu.Items.Add($"{lic.Message}: buy or enter a key...", null, (_, _) => ShowLicense());
                menu.Items.Add(new ToolStripSeparator());
            }
            if (_update != null)
            {
                menu.Items.Add($"Orgodesk {VersionText(_update.Version)} is out: open the download page", null, (_, _) => OpenUpdatePage());
                menu.Items.Add(new ToolStripSeparator());
            }
            menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
            menu.Items.Add("Layout snapshots...", null, (_, _) => ShowSnapshots());
            menu.Items.Add("Sort icons by rules now", null, (_, _) => _surface.ApplyRulesNow());
            menu.Items.Add(new ToolStripSeparator());
            var presets = new ToolStripMenuItem("Presets");
            var active = Core.Fences.Presets.Active(_model.Layout);
            foreach (var p in _model.Layout.Presets)
            {
                var id = p.Id;
                var mi = new ToolStripMenuItem(p.Name) { Checked = p == active };
                mi.Click += (_, _) => _surface.ApplyPreset(id);
                presets.DropDownItems.Add(mi);
            }
            if (_model.Layout.Presets.Count > 0) presets.DropDownItems.Add(new ToolStripSeparator());
            presets.DropDownItems.Add("Save current desktop as preset...", null, (_, _) => _surface.SavePreset());
            if (active != null) presets.DropDownItems.Add($"Update \"{active.Name}\" from current desktop", null, (_, _) => _surface.UpdateActivePreset());
            menu.Items.Add(presets);
            if (!_model.Settings.EnablePages) goto afterPages;
            var pages = new ToolStripMenuItem("Desktop pages");
            for (int i = 0; i < _model.Layout.Pages.Count; i++)
            {
                int idx = i;
                var mi = new ToolStripMenuItem(_model.Layout.Pages[i].Name) { Checked = i == _model.Layout.ActivePageIndex };
                mi.Click += (_, _) => _surface.GoToPage(idx);
                pages.DropDownItems.Add(mi);
            }
            pages.DropDownItems.Add(new ToolStripSeparator());
            pages.DropDownItems.Add("New page...", null, (_, _) => _surface.AddPage());
            menu.Items.Add(pages);
            afterPages:
            var suspend = new ToolStripMenuItem("Switch off (show the normal desktop)") { Checked = _surface.Suspended };
            suspend.Click += (_, _) => _surface.ToggleSuspend();
            menu.Items.Add(suspend);
            var hide = new ToolStripMenuItem("Hide icons (quick hide)") { Checked = _surface.QuickHidden };
            hide.Click += (_, _) => _surface.ToggleQuickHide();
            menu.Items.Add(hide);
            menu.Items.Add("Refresh icons", null, (_, _) => { _model.Icons.Clear(); _surface.RefreshDesktop(); });
            menu.Items.Add(new ToolStripSeparator());
            if (lic.State != LicenseState.DevBuild) menu.Items.Add("License...", null, (_, _) => ShowLicense());
            var ver = typeof(TrayContext).Assembly.GetName().Version;
            menu.Items.Add(new ToolStripMenuItem($"Orgodesk {ver?.Major}.{ver?.Minor}.{ver?.Build}") { Enabled = false });   // so a support message can name the build
            if (Sales.UpdateUrl.Length > 0) menu.Items.Add("Check for a new version now", null, (_, _) => _ = CheckForUpdateAsync(byHand: true));
            menu.Items.Add("Open settings folder", null, (_, _) => Windows.Shell.ShellActions.OpenPath(_store.RootDir, IntPtr.Zero));
            menu.Items.Add("Open log folder (for support)", null, (_, _) => OpenLogFolder());
            menu.Items.Add("Exit (restore desktop icons)", null, (_, _) => Exit());
        };
        return menu;
    }

    /// <summary>Opens the folder that holds error.log. Says so, with what to do, if it is missing or will not open.</summary>
    private static void OpenLogFolder()
    {
        var dir = Path.GetDirectoryName(Log.Path)!;
        if (!Directory.Exists(dir))
        {
            MessageBox.Show(DialogOwner.Get(), $"The log folder does not exist yet:\n{dir}\n\nOrgodesk creates it the first time it saves your layout or meets a problem, so nothing has been logged. If you are chasing a problem, repeat it and try again.",
                "Orgodesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!Windows.Shell.ShellActions.OpenPath(dir, IntPtr.Zero))
            MessageBox.Show(DialogOwner.Get(), $"Windows would not open the folder. Open File Explorer and paste this into the address bar:\n{dir}",
                "Orgodesk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_model, _store, _surface);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
    }

    private void ShowLicense()
    {
        if (_licenseCheckBusy) return;   // the running check already has a licence window open
        _licenseCheckBusy = true;
        try
        {
            using var dlg = new LicenseForm(_licenseStore, _license, Sales.Config);
            dlg.ShowDialog(DialogOwner.Get());
        }
        finally { _licenseCheckBusy = false; }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        // Wi-Fi is rarely up the moment the PC wakes; check two minutes later.
        try
        {
            _surface.BeginInvoke(new Action(() =>
            {
                var once = new System.Windows.Forms.Timer { Interval = 120_000 };
                once.Tick += (_, _) => { once.Dispose(); _ = CheckLicenseWhileRunningAsync(); };
                once.Start();
            }));
        }
        catch { }
    }

    /// <summary>
    /// Rechecks the key when due, then evaluates the licence. When it can no longer run (trial over, refund, grace
    /// over), shows the licence window as startup does and exits if no key fixes it. A failed network check changes
    /// nothing (GumroadVerifier.Apply), so a paying buyer inside the offline grace is never interrupted.
    /// </summary>
    private async Task CheckLicenseWhileRunningAsync()
    {
        if (_licenseCheckBusy || _exiting) return;
        _licenseCheckBusy = true;
        try
        {
            await RecheckLicenseAsync().ConfigureAwait(true);
            if (_exiting) return;
            var status = LicenseRules.Evaluate(_license, Sales.Config, DateTime.UtcNow);
            if (status.CanRun) return;
            Log.Info($"License state while running: {status.State} ({status.Message})");
            if (!_license.Revoked && !string.IsNullOrWhiteSpace(_license.Key) && _license.LastVerifiedUtc != null)
            {
                // Only the network is missing: never close a paying buyer's copy mid-session. The next start decides.
                _tray.ShowBalloonTip(8000, "Orgodesk", "Orgodesk could not check your license for over 30 days. Connect to the internet; it will check again by itself.", ToolTipIcon.Warning);
                return;
            }
            using (var form = new LicenseForm(_licenseStore, _license, Sales.Config))
            {
                form.ShowDialog(DialogOwner.Get());
                if (form.Status.CanRun) return;
            }
            Exit();
        }
        catch (Exception ex) { Log.Error("License check failed", ex); }
        finally { _licenseCheckBusy = false; }
    }

    /// <summary>Confirms a stored key with Gumroad about once a week. Offline is fine: the grace period covers it.</summary>
    private async Task RecheckLicenseAsync()
    {
        if (!LicenseRules.RecheckDue(_license, Sales.Config, DateTime.UtcNow) || _license.Key is not string key) return;
        try
        {
            var res = await new GumroadVerifier(Sales.Config).VerifyAsync(key, activate: false).ConfigureAwait(true);
            GumroadVerifier.Apply(_license, key, res, DateTime.UtcNow);
            _licenseStore.Save(_license);
            Log.Info($"License recheck: {res.Outcome}");
        }
        catch (Exception ex) { Log.Error("License recheck failed", ex); }
    }

    private static string VersionText(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

    /// <summary>
    /// Reads the seller's version file. The automatic check respects the setting and shows its balloon once per
    /// release; a check asked for from the menu always answers. Nothing is downloaded or installed.
    /// </summary>
    private async Task CheckForUpdateAsync(bool byHand)
    {
        if (!byHand && !_model.Settings.CheckForUpdates) return;
        try
        {
            var current = typeof(TrayContext).Assembly.GetName().Version ?? new Version(0, 0, 0);
            var found = await new UpdateChecker(Sales.UpdateUrl, buyUrl: Sales.BuyUrl).CheckAsync(current).ConfigureAwait(true);
            if (_exiting) return;
            _update = found;
            if (found == null)
            {
                if (byHand) _tray.ShowBalloonTip(6000, "Orgodesk", "No newer version found. If you expected one, check your internet connection and try again later.", ToolTipIcon.Info);
                return;
            }
            var text = VersionText(found.Version);
            if (!byHand && _model.Settings.UpdateNoticeShownFor == text) return;
            _model.Settings.UpdateNoticeShownFor = text;
            try { _store.SaveSettings(_model.Settings); } catch (Exception ex) { Log.Error("Save settings failed", ex); }
            Log.Info($"Update available: {text}");
            _updateBalloonUp = true;
            var notes = found.Notes.Length > 0 ? found.Notes + " " : "";
            _tray.ShowBalloonTip(10000, $"Orgodesk {text} is out", notes + "Click here, or use the tray menu, to open the download page.", ToolTipIcon.Info);
        }
        catch (Exception ex) { Log.Error("Update check failed", ex); }
    }

    private void OpenUpdatePage()
    {
        _updateBalloonUp = false;
        if (_update != null) Windows.Shell.ShellActions.OpenPath(_update.Url, IntPtr.Zero);
    }

    private void ShowSnapshots()
    {
        using var dlg = new SnapshotsForm(_store, _model, _surface);
        dlg.ShowDialog(DialogOwner.Get());
    }

    public void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        _tray.Visible = false;
        _mouseHook?.Dispose();
        _mouseHook = null;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _dailyTimer?.Dispose();
        _dailyTimer = null;
        _exitWait?.Unregister(null);
        _surface.Shutdown();
        try { DesktopWindows.SetDesktopIconsVisible(true); } catch { }
        ClearHiddenFlag();
        _model.Dispose();
        ExitThread();
    }

    private static Icon MakeTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(Color.FromArgb(230, 40, 40, 40));
            using var title = new SolidBrush(Color.FromArgb(255, 70, 130, 220));
            using var path = SurfaceRenderer.RoundedRect(new Rectangle(2, 4, 28, 24), 5);
            g.FillPath(body, path);
            g.FillRectangle(title, 2, 4, 28, 7);
            using var dot = new SolidBrush(Color.FromArgb(255, 230, 230, 230));
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 3; c++)
                    g.FillRectangle(dot, 6 + c * 8, 14 + r * 7, 5, 5);
        }
        var h = bmp.GetHicon();
        using var tmp = Icon.FromHandle(h);
        var icon = (Icon)tmp.Clone();
        Windows.Shell.User32.DestroyIcon(h);
        return icon;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _icon.Dispose();
            _surface.Dispose();
            _exitEvent?.Dispose();
        }
        base.Dispose(disposing);
    }
}
