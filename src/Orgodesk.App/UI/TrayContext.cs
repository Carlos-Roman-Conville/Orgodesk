using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Orgodesk.Core.Persistence;
using Orgodesk.Windows.Desktop;

namespace Orgodesk.App.UI;

/// <summary>Owns the tray icon, the surface, the store and the dialogs. Lives for the whole run.</summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly LayoutStore _store;
    private readonly DesktopModel _model;
    private readonly DesktopSurface _surface;
    private readonly NotifyIcon _tray;
    private readonly Icon _icon;
    private SettingsForm? _settingsForm;
    private DesktopMouseHook? _mouseHook;
    private EventWaitHandle? _exitEvent;
    private RegisteredWaitHandle? _exitWait;
    private bool _exiting;

    private static string HiddenFlagPath => Path.Combine(LayoutStore.DefaultRoot, "icons-hidden.flag");
    public static void ClearHiddenFlag() { try { File.Delete(HiddenFlagPath); } catch { } }

    public TrayContext(LayoutStore store)
    {
        _store = store;
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
    }

    /// <summary>Hides the real desktop icons and attaches the surface. False if the desktop could not be found.</summary>
    public bool Start()
    {
        if (!_surface.Attach())
        {
            MessageBox.Show("Could not find the Windows desktop window (SHELLDLL_DefView). Is Explorer running?",
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
        return true;
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
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
            menu.Items.Add("Open settings folder", null, (_, _) => Windows.Shell.ShellActions.OpenPath(_store.RootDir, IntPtr.Zero));
            menu.Items.Add("Exit (restore desktop icons)", null, (_, _) => Exit());
        };
        return menu;
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

    private void ShowSnapshots()
    {
        using var dlg = new SnapshotsForm(_store, _model, _surface);
        dlg.ShowDialog();
    }

    public void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        _tray.Visible = false;
        _mouseHook?.Dispose();
        _mouseHook = null;
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
