using System.Drawing;
using System.Windows.Forms;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Persistence;
using Orgodesk.Windows.Desktop;
using Orgodesk.Windows.Shell;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace Orgodesk.App.UI;

/// <summary>
/// The icon surface: a layered child window of Explorer's SHELLDLL_DefView that covers the whole virtual
/// screen, draws fences and icons, and handles all mouse, keyboard, menu and drag-and-drop input.
/// All layout coordinates are screen pixels; the window's origin is subtracted only when drawing.
/// </summary>
public sealed class DesktopSurface : Control
{
    private enum Mode { None, Pending, MoveFence, ResizeFence, Lasso, CreateFence, Swipe }
    [Flags] private enum Edge { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }
    private enum HitKind { Nothing, FenceTitle, FenceBody, FenceEdge, Item }
    private readonly record struct Hit(HitKind Kind, Fence? Fence, Edge Edges, ItemRef? Item, Rectangle Cell);

    // Our own ids on the shell background menu (below ContextMenuSession.ShellIdFirst).
    private const uint IdNewFence = 1, IdNewPortal = 2, IdSortRules = 3, IdQuickHide = 4, IdSnapshots = 5,
        IdSettings = 6, IdExit = 7, IdRefresh = 8, IdNewPage = 9, IdRenamePage = 10, IdDeletePage = 11, IdSuspend = 12,
        IdSavePreset = 13, IdUpdatePreset = 14, IdPageBase = 100, IdPresetBase = 200, IdDeletePresetBase = 400;

    private readonly DesktopModel _model;
    private readonly LayoutStore _store;
    private readonly SurfaceRenderer _renderer;
    private ShellChangeWatcher? _watcher;
    private readonly Timer _watchdog = new() { Interval = 2000 };
    private readonly Timer _saveTimer = new() { Interval = 600 };
    private readonly Timer _refreshTimer = new() { Interval = 350 };
    private readonly Timer _hudTimer = new() { Interval = 1200 };
    private readonly Timer _peekOpenTimer = new() { Interval = 150 };
    private readonly Timer _peekCloseTimer = new() { Interval = 400 };
    private Fence? _peekCandidate;
    private bool _isDragOver;

    private IntPtr _parentHwnd;
    private uint _attachedThread;
    private Rectangle _screen;
    private Point _origin;
    private DibSurface? _dib;
    private bool _renderQueued;
    private bool _closing;
    private DateTime _lastHideAttempt = DateTime.MinValue;

    private readonly HashSet<ItemRef> _selection = new();
    private ItemRef? _hover;
    private string? _hoverFence;
    private bool _quickHidden;
    private bool _suspended;
    private string? _hud;

    private Mode _mode;
    private MouseButtons _downButton;
    private Point _down;
    private Hit _downHit;
    private Fence? _activeFence;
    private Rectangle _activeStart;
    private Edge _resizeEdges;
    private Rectangle _lasso, _createRect;

    private HashSet<ItemRef>? _dragging;
    private Point _dragGrabOffset;
    private DropTarget? _drop;
    private bool _droppedHere;
    private ContextMenuSession? _shellMenu;

    public event EventHandler? ExitRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? SnapshotsRequested;
    public event EventHandler? LayoutChanged;

    public DesktopModel Model => _model;
    public bool QuickHidden => _quickHidden;
    public bool Suspended => _suspended;
    public bool IsAttached => IsHandleCreated && _parentHwnd != IntPtr.Zero && User32.IsWindow(_parentHwnd);

    public DesktopSurface(DesktopModel model, LayoutStore store)
    {
        _model = model;
        _store = store;
        _renderer = new SurfaceRenderer(model);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque
               | ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        AllowDrop = true;
        TabStop = true;

        _watchdog.Tick += (_, _) => Watchdog();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); RefreshDesktop(); };
        _hudTimer.Tick += (_, _) => { _hudTimer.Stop(); _hud = null; RequestRender(); };
        _peekOpenTimer.Tick += (_, _) => { _peekOpenTimer.Stop(); OpenPeek(); };
        _peekCloseTimer.Tick += (_, _) => { _peekCloseTimer.Stop(); ClosePeeks(); };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    // ---------------------------------------------------------------- window plumbing

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Parent = _parentHwnd;
            cp.Style = unchecked((int)(Const.WS_CHILD | Const.WS_VISIBLE | Const.WS_CLIPSIBLINGS));
            cp.ExStyle |= unchecked((int)Const.WS_EX_LAYERED);
            cp.X = 0; cp.Y = 0;
            cp.Width = Math.Max(1, _screen.Width);
            cp.Height = Math.Max(1, _screen.Height);
            return cp;
        }
    }

    /// <summary>Finds the desktop and creates (or re-creates) the surface inside it.</summary>
    public bool Attach()
    {
        var defView = DesktopWindows.FindDefView();
        if (defView == IntPtr.Zero) return false;
        _parentHwnd = defView;
        _screen = ScreenInfo.VirtualScreen;
        if (IsHandleCreated) RecreateHandle(); else CreateHandle();
        if (_watcher == null)
        {
            try
            {
                _watcher = new ShellChangeWatcher();
                _watcher.Changed += evt =>
                {
                    if (evt == Const.SHCNE_UPDATEIMAGE || evt == Const.SHCNE_ASSOCCHANGED) _model.Icons.Clear();
                    ScheduleRefresh();
                };
            }
            catch (Exception ex)
            {
                Log.Error("Shell change watcher failed; falling back to manual refresh", ex);
            }
        }
        _watchdog.Start();
        ReportLoadProblems();
        return IsHandleCreated;
    }

    /// <summary>Logs and shows any problem met while loading the layout or settings (FC-28, FC-29). Shown once.</summary>
    private void ReportLoadProblems()
    {
        var problems = _store.TakeLoadProblems();
        if (problems.Count == 0) return;
        foreach (var p in problems) Log.Error(p);
        var text = string.Join(Environment.NewLine + Environment.NewLine, problems);
        void Show() => MessageBox.Show(text, "Orgodesk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (IsHandleCreated) BeginInvoke(new Action(Show)); else Show();
    }

    /// <summary>
    /// Converts the layout when the display scale differs from the one it was laid out at (FC-32).
    /// Returns true if the layout changed.
    /// </summary>
    private bool EnsureGridScale(Rectangle area, LayoutMetrics m)
    {
        var scale = ScreenInfo.ScaleAt(new Point(area.X + area.Width / 2, area.Y + area.Height / 2));
        return LayoutReconciler.Rescale(_model.Layout, scale, area, m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        PositionWindow();
        var thread = DesktopWindows.ThreadOf(_parentHwnd);
        if (thread != 0 && thread != _attachedThread)
        {
            User32.AttachThreadInput(Kernel32.GetCurrentThreadId(), thread, true);
            _attachedThread = thread;
        }
        RebuildDib();
        RequestRender();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_attachedThread != 0)
        {
            User32.AttachThreadInput(Kernel32.GetCurrentThreadId(), _attachedThread, false);
            _attachedThread = 0;
        }
        base.OnHandleDestroyed(e);
    }

    private void PositionWindow()
    {
        _screen = ScreenInfo.VirtualScreen;
        var parentRect = DesktopWindows.WindowRect(_parentHwnd);
        int x = _screen.X - parentRect.X, y = _screen.Y - parentRect.Y;
        User32.SetWindowPos(Handle, Const.HWND_TOP, x, y, _screen.Width, _screen.Height, Const.SWP_SHOWWINDOW | Const.SWP_NOACTIVATE);
        _origin = PointToScreen(Point.Empty);
    }

    private void RebuildDib()
    {
        _dib?.Dispose();
        _dib = new DibSurface(_screen.Width, _screen.Height);
    }

    private void Watchdog()
    {
        if (_closing || _suspended) return;
        if (!IsHandleCreated || !User32.IsWindow(_parentHwnd) || User32.GetParent(Handle) != _parentHwnd)
        {
            // Explorer restarted or the desktop window went away: find it again.
            var dv = DesktopWindows.FindDefView();
            if (dv == IntPtr.Zero) return;
            _parentHwnd = dv;
            _screen = ScreenInfo.VirtualScreen;
            if (IsHandleCreated) RecreateHandle(); else CreateHandle();
            _lastHideAttempt = DateTime.UtcNow;
            DesktopWindows.SetDesktopIconsVisible(false);
            return;
        }
        // Explorer re-shows its icon list on some events (refresh, theme or display change); hide it again.
        if (DesktopWindows.AreDesktopIconsVisible() && (DateTime.UtcNow - _lastHideAttempt) > TimeSpan.FromSeconds(2))
        {
            _lastHideAttempt = DateTime.UtcNow;
            DesktopWindows.SetDesktopIconsVisible(false);
        }
        if (_screen != ScreenInfo.VirtualScreen) OnDisplaySettingsChanged(this, EventArgs.Empty);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (!IsHandleCreated || _closing) return;
        PositionWindow();
        RebuildDib();
        var work = ScreenInfo.PrimaryWorkArea;
        bool rescaled = EnsureGridScale(work, _renderer.MetricsAt(new Point(work.X + work.Width / 2, work.Y + work.Height / 2)));
        var area = ScreenInfo.VirtualScreen;
        if (LayoutReconciler.ClampToArea(_model.Layout, area, _renderer.MetricsAt(area.Location)) > 0 || rescaled) SaveSoon();
        _renderer.ClearFonts();
        RequestRender();
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Desktop || e.Category == UserPreferenceCategory.General)
            RequestRender();
    }

    protected override void OnPaint(PaintEventArgs e) { /* layered: painted with UpdateLayeredWindow */ }
    protected override void OnPaintBackground(PaintEventArgs pevent) { }

    protected override void WndProc(ref Message m)
    {
        if (_shellMenu != null && _shellMenu.HandleMenuMessage(ref m)) return;
        switch (m.Msg)
        {
            case 0x007B: // WM_CONTEXTMENU: never let it bubble to the real desktop, which would show its own menu too
                m.Result = IntPtr.Zero;
                return;
            case Const.WM_DPICHANGED_AFTERPARENT:
                _renderer.ClearFonts();
                {
                    var work = ScreenInfo.PrimaryWorkArea;
                    if (EnsureGridScale(work, _renderer.MetricsAt(new Point(work.X + work.Width / 2, work.Y + work.Height / 2)))) SaveSoon();
                }
                RequestRender();
                break;
        }
        base.WndProc(ref m);
    }

    // ---------------------------------------------------------------- rendering

    public void RequestRender()
    {
        if (_renderQueued || !IsHandleCreated || _closing) return;
        _renderQueued = true;
        try { BeginInvoke(new Action(Render)); }
        catch { _renderQueued = false; }
    }

    private void Render()
    {
        _renderQueued = false;
        if (_dib == null || !IsHandleCreated || _closing) return;
        var frame = new FrameState
        {
            Selection = _selection,
            Hover = _hover,
            HoverFenceId = _hoverFence,
            Lasso = _mode == Mode.Lasso ? _lasso : null,
            CreateRect = _mode == Mode.CreateFence ? _createRect : null,
            Drop = _drop,
            QuickHidden = _quickHidden,
            Hud = _hud,
            Dragging = _dragging,
        };
        try
        {
            using var g = Graphics.FromImage(_dib.Bitmap);
            _renderer.Render(g, _origin, frame);
            _dib.Present(Handle);
        }
        catch (Exception ex)
        {
            Log.Error("Render failed", ex);
        }
    }

    private void ShowHud(string text)
    {
        _hud = text;
        _hudTimer.Stop();
        _hudTimer.Start();
        RequestRender();
    }

    // ---------------------------------------------------------------- model / layout

    public void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    /// <summary>Re-reads the desktop and portals and reconciles the layout.</summary>
    public void RefreshDesktop()
    {
        if (_closing) return;
        try
        {
            var facts = _model.RefreshDesktop();
            var area = ScreenInfo.PrimaryWorkArea;
            var m = _renderer.MetricsAt(new Point(area.X + area.Width / 2, area.Y + area.Height / 2));
            bool changed = EnsureGridScale(area, m);
            changed |= ApplyPending(area, m);
            var result = LayoutReconciler.Reconcile(_model.Layout, facts, area, m);
            changed |= result.Changed;
            changed |= LayoutReconciler.EvictFreeIconsUnderFences(_model.Layout.ActivePage, area, m) > 0;
            _model.RefreshPortals();
            _watcher?.SetWatchedFolders(_model.PortalPaths());
            _selection.RemoveWhere(r => _model.Item(r) == null);
            if (_hover is { } h && _model.Item(h) == null) _hover = null;
            if (changed) { SaveSoon(); LayoutChanged?.Invoke(this, EventArgs.Empty); }
        }
        catch (Exception ex)
        {
            Log.Error("Refresh failed", ex);
        }
        RequestRender();
    }

    private bool ApplyPending(Rectangle area, LayoutMetrics m)
    {
        bool changed = false;
        foreach (var key in _model.Pending.Keys.ToList())
        {
            var p = _model.Pending[key];
            if (p.Expired) { _model.Pending.Remove(key); continue; }
            if (!_model.DesktopItems.ContainsKey(key)) continue;
            _model.Pending.Remove(key);
            foreach (var page in _model.Layout.Pages) page.Remove(key);
            var fence = p.FenceId != null ? _model.Layout.FindFence(p.FenceId) : null;
            if (fence != null && !fence.IsPortal) fence.Items.Add(key);
            else if (p.FreePosition is { } fp) _model.Layout.ActivePage.FreeItems[key] = _model.Settings.SnapFreeIconsToGrid ? FreeGrid.Snap(fp, area, m) : fp;
            else _model.Layout.ActivePage.FreeItems[key] = LayoutReconciler.NextFreeSlot(_model.Layout.ActivePage, area, m);
            changed = true;
        }
        return changed;
    }

    /// <summary>Moves free icons out from under every fence on the active page.</summary>
    private void EvictUnderFences()
    {
        var area = ScreenInfo.PrimaryWorkArea;
        var m = _renderer.MetricsAt(new Point(area.X + area.Width / 2, area.Y + area.Height / 2));
        if (LayoutReconciler.EvictFreeIconsUnderFences(_model.Layout.ActivePage, area, m) > 0) SaveSoon();
    }

    public void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        try { _store.SaveLayout(_model.Layout); }
        catch (Exception ex) { Log.Error("Save failed", ex); }
    }

    /// <summary>Re-reads settings-dependent caches and redraws.</summary>
    public void ApplySettings()
    {
        _renderer.ClearFonts();
        _model.Icons.Clear();
        RequestRender();
    }

    /// <summary>Swaps in a different layout (snapshot restore) and reconciles it with the desktop.</summary>
    public void ReplaceLayout(Layout layout)
    {
        _model.Layout = layout;
        _selection.Clear();
        RefreshDesktop();
        SaveSoon();
    }

    /// <summary>Switches the clone off: hides the surface and shows the normal Windows desktop. Layout stays loaded.</summary>
    public void Suspend()
    {
        if (_suspended) return;
        _suspended = true;
        _selection.Clear();
        if (IsHandleCreated) User32.ShowWindow(Handle, 0);
        DesktopWindows.SetDesktopIconsVisible(true);
    }

    /// <summary>Switches the clone back on after <see cref="Suspend"/>.</summary>
    public void Resume()
    {
        if (!_suspended) return;
        _suspended = false;
        if (!IsHandleCreated || !User32.IsWindow(_parentHwnd)) { Attach(); }
        DesktopWindows.SetDesktopIconsVisible(false);
        if (IsHandleCreated)
        {
            User32.ShowWindow(Handle, 8);
            User32.SetWindowPos(Handle, Const.HWND_TOP, 0, 0, 0, 0, Const.SWP_NOMOVE | Const.SWP_NOSIZE | Const.SWP_NOACTIVATE);
        }
        RefreshDesktop();
    }

    public void ToggleSuspend()
    {
        if (_suspended) Resume(); else Suspend();
    }

    public void ToggleQuickHide()
    {
        _quickHidden = !_quickHidden;
        RequestRender();
    }

    public Fence CreateFence(Rectangle bounds, string? title = null, string? portalPath = null)
    {
        var area = ScreenInfo.VirtualScreen;
        var m = _renderer.MetricsAt(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
        var f = new Fence
        {
            Title = title ?? (portalPath != null ? Path.GetFileName(portalPath.TrimEnd('\\')) : "New Orgo"),
            Bounds = FenceLayoutEngine.ClampBounds(bounds, area, m),
            PortalPath = portalPath,
        };
        if (!f.IsPortal)
        {
            // Icons the new fence covers become its first members, like Fences does.
            var page = _model.Layout.ActivePage;
            foreach (var (key, pos) in page.FreeItems.ToList())
            {
                if (f.Bounds.IntersectsWith(FreeGrid.CellAt(pos, _renderer.MetricsAt(pos))))
                {
                    page.FreeItems.Remove(key);
                    f.Items.Add(key);
                }
            }
        }
        _model.Layout.ActivePage.Fences.Add(f);
        EvictUnderFences();
        if (f.IsPortal) { _model.RefreshPortals(); _watcher?.SetWatchedFolders(_model.PortalPaths()); }
        SaveSoon();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        RequestRender();
        return f;
    }

    public void DeleteFence(Fence fence)
    {
        var page = _model.Layout.PageOfFence(fence.Id);
        if (page == null) return;
        var area = ScreenInfo.PrimaryWorkArea;
        LayoutReconciler.ReleaseFence(page, fence, area, _renderer.MetricsAt(area.Location));
        if (fence.IsPortal) { _model.RefreshPortals(); _watcher?.SetWatchedFolders(_model.PortalPaths()); }
        SaveSoon();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        RequestRender();
    }

    public int ApplyRulesNow()
    {
        var facts = _model.DesktopItems.Values.Select(i => i.ToFacts()).ToList();
        int moved = LayoutReconciler.ApplyRules(_model.Layout, facts);
        if (moved > 0) { SaveSoon(); RequestRender(); }
        ShowHud(moved == 1 ? "1 item sorted" : $"{moved} items sorted");
        return moved;
    }

    // presets

    public void SavePreset()
    {
        var name = Prompt.Show("Save preset", "Preset name (an existing name is replaced):", Presets.Active(_model.Layout)?.Name ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var p = Presets.Save(_model.Layout, name);
        SaveSoon();
        ShowHud($"Preset saved: {p.Name}");
    }

    public void UpdateActivePreset()
    {
        var active = Presets.Active(_model.Layout);
        if (active == null) { SavePreset(); return; }
        Presets.Update(_model.Layout, active.Id);
        SaveSoon();
        ShowHud($"Preset updated: {active.Name}");
    }

    public void ApplyPreset(int index)
    {
        var list = _model.Layout.Presets;
        if (index < 0 || index >= list.Count) return;
        ApplyPreset(list[index].Id);
    }

    public void ApplyPreset(string presetId)
    {
        if (!Presets.Apply(_model.Layout, presetId)) return;
        _selection.Clear();
        var name = Presets.Active(_model.Layout)?.Name ?? "";
        RefreshDesktop(); // places icons the preset does not know about, drops ones that are gone
        var area = ScreenInfo.VirtualScreen;
        LayoutReconciler.ClampToArea(_model.Layout, area, _renderer.MetricsAt(area.Location));
        EvictUnderFences();
        _model.RefreshPortals();
        _watcher?.SetWatchedFolders(_model.PortalPaths());
        SaveSoon();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        ShowHud($"Preset: {name}");
    }

    public void DeletePreset(int index)
    {
        var list = _model.Layout.Presets;
        if (index < 0 || index >= list.Count) return;
        var p = list[index];
        if (MessageBox.Show($"Delete preset \"{p.Name}\"? The desktop stays as it is.", "Orgodesk",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        Presets.Delete(_model.Layout, p.Id);
        SaveSoon();
        RequestRender();
    }

    // pages

    public void GoToPage(int index)
    {
        var pages = _model.Layout.Pages;
        if (pages.Count == 0) return;
        index = ((index % pages.Count) + pages.Count) % pages.Count;
        if (index == _model.Layout.ActivePageIndex) return;
        _model.Layout.ActivePageIndex = index;
        _selection.Clear();
        SaveSoon();
        ShowHud(pages[index].Name);
        RequestRender();
    }

    public void NextPage() => GoToPage(_model.Layout.ActivePageIndex + 1);
    public void PreviousPage() => GoToPage(_model.Layout.ActivePageIndex - 1);

    public void AddPage()
    {
        var name = Prompt.Show("New page", "Page name:", $"Page {_model.Layout.Pages.Count + 1}");
        if (name == null) return;
        _model.Layout.Pages.Add(new Page { Name = name });
        GoToPage(_model.Layout.Pages.Count - 1);
        SaveSoon();
    }

    public void RenamePage()
    {
        var page = _model.Layout.ActivePage;
        var name = Prompt.Show("Rename page", "Page name:", page.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        page.Name = name;
        SaveSoon();
        RequestRender();
    }

    public void DeletePage()
    {
        var layout = _model.Layout;
        if (layout.Pages.Count <= 1) return;
        var page = layout.ActivePage;
        if (MessageBox.Show($"Delete page \"{page.Name}\"? Its Orgos are removed and its icons move to the first page.",
                "Orgodesk", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        var area = ScreenInfo.PrimaryWorkArea;
        var m = _renderer.MetricsAt(area.Location);
        layout.Pages.Remove(page);
        layout.ActivePageIndex = 0;
        var first = layout.ActivePage;
        foreach (var key in page.FreeItems.Keys.Concat(page.Fences.SelectMany(f => f.Items)).ToList())
            first.FreeItems[key] = LayoutReconciler.NextFreeSlot(first, area, m);
        _selection.Clear();
        SaveSoon();
        RequestRender();
    }

    // ---------------------------------------------------------------- snapping

    /// <summary>Snaps a fence rectangle to the screen work area and the other fences, unless snapping is off or Alt is held.</summary>
    private Rectangle SnapFence(Fence fence, Rectangle rect, LayoutMetrics m, FenceSnapper.Edges edges)
    {
        if (!_model.Settings.SnapFences || ModifierKeys.HasFlag(Keys.Alt)) return rect;
        double scale = m.CellWidth / (double)Math.Max(1, _model.Settings.CellWidth);
        int distance = (int)Math.Round(Math.Max(1, _model.Settings.SnapDistance) * scale);
        var others = _model.Layout.ActivePage.Fences.Where(f => f != fence)
            .Select(f => FenceLayoutEngine.VisibleBounds(f, _renderer.MetricsAt(f.Bounds.Location)));
        var area = ScreenInfo.WorkAreaAt(new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
        return FenceSnapper.Snap(rect, others, area, distance, m.Padding, edges);
    }

    private static FenceSnapper.Edges ToSnapEdges(Edge e)
    {
        var r = FenceSnapper.Edges.None;
        if (e.HasFlag(Edge.Left)) r |= FenceSnapper.Edges.Left;
        if (e.HasFlag(Edge.Right)) r |= FenceSnapper.Edges.Right;
        if (e.HasFlag(Edge.Top)) r |= FenceSnapper.Edges.Top;
        if (e.HasFlag(Edge.Bottom)) r |= FenceSnapper.Edges.Bottom;
        return r;
    }

    // ---------------------------------------------------------------- auto-roll (peek)

    /// <summary>The topmost fence whose visible bounds contain the point, or null.</summary>
    private Fence? FenceAt(Point p)
    {
        var page = _model.Layout.ActivePage;
        for (int i = page.Fences.Count - 1; i >= 0; i--)
        {
            var f = page.Fences[i];
            var m = _renderer.MetricsAt(new Point(f.Bounds.X + f.Bounds.Width / 2, f.Bounds.Y + f.Bounds.Height / 2));
            if (FenceLayoutEngine.VisibleBounds(f, m).Contains(p)) return f;
        }
        return null;
    }

    /// <summary>Opens an auto-roll fence under the mouse after a short delay; closes open ones once the mouse has left them.</summary>
    private void UpdatePeek(Fence? under, Point p)
    {
        if (under != null && under.AutoRollUp && under.RolledUp && !under.Peeking)
        {
            if (_peekCandidate != under) { _peekCandidate = under; _peekOpenTimer.Stop(); _peekOpenTimer.Start(); }
        }
        else if (under == null || !under.AutoRollUp)
        {
            _peekCandidate = null;
            _peekOpenTimer.Stop();
        }

        bool anyOpenElsewhere = _model.Layout.ActivePage.Fences.Any(f => f.Peeking && (under != f || !f.Bounds.Contains(p)));
        if (anyOpenElsewhere) { if (!_peekCloseTimer.Enabled) _peekCloseTimer.Start(); }
        else _peekCloseTimer.Stop();
    }

    private void OpenPeek()
    {
        var f = _peekCandidate;
        _peekCandidate = null;
        if (f == null || !f.AutoRollUp || !f.RolledUp || f.Peeking) return;
        // Only open if the mouse is still over the fence.
        if (!FenceLayoutEngine.VisibleBounds(f, _renderer.MetricsAt(f.Bounds.Location)).Contains(Cursor.Position)) return;
        f.Peeking = true;
        RequestRender();
    }

    private void ClosePeeks()
    {
        var mouse = Cursor.Position;
        bool changed = false;
        foreach (var f in _model.Layout.ActivePage.Fences)
        {
            if (!f.Peeking) continue;
            if (f.Bounds.Contains(mouse) && _dragging == null && _mode == Mode.None && !_isDragOver) { _peekCloseTimer.Start(); continue; }
            f.Peeking = false;
            changed = true;
        }
        if (changed) RequestRender();
    }

    // ---------------------------------------------------------------- hit testing

    private Hit HitTest(Point p)
    {
        var page = _model.Layout.ActivePage;
        for (int i = page.Fences.Count - 1; i >= 0; i--)
        {
            var f = page.Fences[i];
            var m = _renderer.MetricsAt(new Point(f.Bounds.X + f.Bounds.Width / 2, f.Bounds.Y + f.Bounds.Height / 2));
            var vb = FenceLayoutEngine.VisibleBounds(f, m);
            if (!vb.Contains(p)) continue;
            if (!f.IsCollapsed)
            {
                var edges = EdgesAt(p, f.Bounds, Math.Max(4, m.Padding));
                if (edges != Edge.None) return new Hit(HitKind.FenceEdge, f, edges, null, Rectangle.Empty);
            }
            if (FenceLayoutEngine.TitleBar(f, m).Contains(p)) return new Hit(HitKind.FenceTitle, f, Edge.None, null, Rectangle.Empty);
            var content = FenceLayoutEngine.ContentArea(f, m);
            if (content.Contains(p))
            {
                var items = _model.ItemsOf(f);
                var cells = FenceLayoutEngine.Arrange(items.Select(x => x.Key).ToList(), content, m, f.ScrollOffset);
                for (int c = 0; c < cells.Count; c++)
                    if (cells[c].Visible && cells[c].Bounds.Contains(p))
                        return new Hit(HitKind.Item, f, Edge.None, new ItemRef(f.IsPortal ? f.Id : null, items[c].Key), cells[c].Bounds);
            }
            return new Hit(HitKind.FenceBody, f, Edge.None, null, Rectangle.Empty);
        }
        foreach (var (key, pos) in page.FreeItems)
        {
            if (!_model.DesktopItems.ContainsKey(key)) continue;
            var cell = FreeGrid.CellAt(pos, _renderer.MetricsAt(pos));
            if (cell.Contains(p)) return new Hit(HitKind.Item, null, Edge.None, new ItemRef(null, key), cell);
        }
        return new Hit(HitKind.Nothing, null, Edge.None, null, Rectangle.Empty);
    }

    private static Edge EdgesAt(Point p, Rectangle b, int zone)
    {
        var e = Edge.None;
        if (p.X < b.X + zone) e |= Edge.Left;
        if (p.X >= b.Right - zone) e |= Edge.Right;
        if (p.Y < b.Y + zone) e |= Edge.Top;
        if (p.Y >= b.Bottom - zone) e |= Edge.Bottom;
        return e;
    }

    private static Cursor CursorFor(Edge e)
    {
        bool l = e.HasFlag(Edge.Left), r = e.HasFlag(Edge.Right), t = e.HasFlag(Edge.Top), b = e.HasFlag(Edge.Bottom);
        if ((l && t) || (r && b)) return Cursors.SizeNWSE;
        if ((l && b) || (r && t)) return Cursors.SizeNESW;
        if (l || r) return Cursors.SizeWE;
        if (t || b) return Cursors.SizeNS;
        return Cursors.Default;
    }

    private Point ToScreen(MouseEventArgs e) => PointToScreen(e.Location);

    private bool InSwipeZone(Point p)
    {
        if (!_model.Settings.EnablePages || _model.Layout.Pages.Count < 2) return false;
        var mon = Screen.FromPoint(p).Bounds;
        int zone = (int)Math.Round(24 * ScreenInfo.ScaleAt(p));
        return p.X < mon.X + zone || p.X >= mon.Right - zone;
    }

    // ---------------------------------------------------------------- mouse

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_quickHidden) return;
        User32.SetFocus(Handle);
        var p = ToScreen(e);
        var hit = HitTest(p);
        _down = p;
        _downButton = e.Button;
        _downHit = hit;
        _mode = Mode.Pending;
        bool ctrl = ModifierKeys.HasFlag(Keys.Control);

        switch (hit.Kind)
        {
            case HitKind.Item:
            {
                var r = hit.Item!.Value;
                if (ctrl)
                {
                    if (e.Button == MouseButtons.Left) { if (!_selection.Add(r)) _selection.Remove(r); }
                    else _selection.Add(r);
                }
                else if (!_selection.Contains(r))
                {
                    _selection.Clear();
                    _selection.Add(r);
                }
                break;
            }
            case HitKind.FenceTitle:
            case HitKind.FenceEdge:
            case HitKind.FenceBody:
                if (!ctrl) _selection.Clear();
                _activeFence = hit.Fence;
                _activeStart = hit.Fence!.Bounds;
                _resizeEdges = hit.Edges;
                break;
            default:
                if (!ctrl) _selection.Clear();
                break;
        }
        RequestRender();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_quickHidden) return;
        var p = ToScreen(e);

        if (_mode == Mode.Pending)
        {
            var dragSize = SystemInformation.DragSize;
            if (Math.Abs(p.X - _down.X) < dragSize.Width / 2 && Math.Abs(p.Y - _down.Y) < dragSize.Height / 2) return;
            BeginDrag(p);
            return;
        }

        switch (_mode)
        {
            case Mode.MoveFence:
            {
                var f = _activeFence!;
                var nb = _activeStart;
                nb.Offset(p.X - _down.X, p.Y - _down.Y);
                var m = _renderer.MetricsAt(new Point(nb.X + nb.Width / 2, nb.Y + nb.Height / 2));
                nb = SnapFence(f, nb, m, FenceSnapper.Edges.All);
                f.Bounds = FenceLayoutEngine.ClampBounds(nb, ScreenInfo.VirtualScreen, m);
                RequestRender();
                return;
            }
            case Mode.ResizeFence:
            {
                var f = _activeFence!;
                int dx = p.X - _down.X, dy = p.Y - _down.Y;
                int l = _activeStart.Left, t = _activeStart.Top, r = _activeStart.Right, b = _activeStart.Bottom;
                var m = _renderer.MetricsAt(new Point(_activeStart.X + _activeStart.Width / 2, _activeStart.Y + _activeStart.Height / 2));
                int minW = FenceLayoutEngine.MinFenceWidth, minH = m.TitleBarHeight + m.CellHeight + 2 * m.Padding;
                if (_resizeEdges.HasFlag(Edge.Left)) l = Math.Min(l + dx, r - minW);
                if (_resizeEdges.HasFlag(Edge.Right)) r = Math.Max(r + dx, l + minW);
                if (_resizeEdges.HasFlag(Edge.Top)) t = Math.Min(t + dy, b - minH);
                if (_resizeEdges.HasFlag(Edge.Bottom)) b = Math.Max(b + dy, t + minH);
                var resized = SnapFence(f, Rectangle.FromLTRB(l, t, r, b), m, ToSnapEdges(_resizeEdges));
                f.Bounds = FenceLayoutEngine.ClampBounds(resized, ScreenInfo.VirtualScreen, m);
                RequestRender();
                return;
            }
            case Mode.Lasso:
                _lasso = Normalize(_down, p);
                RequestRender();
                return;
            case Mode.CreateFence:
                _createRect = Normalize(_down, p);
                RequestRender();
                return;
            case Mode.Swipe:
                return;
        }

        // Plain hover
        var hit = HitTest(p);
        UpdatePeek(hit.Fence, p);
        Cursor = hit.Kind == HitKind.FenceEdge ? CursorFor(hit.Edges) : Cursors.Default;
        var newHover = hit.Kind == HitKind.Item ? hit.Item : null;
        var newHoverFence = hit.Kind is HitKind.FenceTitle or HitKind.FenceBody or HitKind.FenceEdge or HitKind.Item ? hit.Fence?.Id : null;
        if (newHover != _hover || newHoverFence != _hoverFence)
        {
            _hover = newHover;
            _hoverFence = newHoverFence;
            RequestRender();
        }
    }

    private void BeginDrag(Point p)
    {
        var hit = _downHit;
        if (_downButton == MouseButtons.Left)
        {
            switch (hit.Kind)
            {
                case HitKind.Item:
                    _mode = Mode.None;
                    BeginItemDrag(hit);
                    return;
                case HitKind.FenceTitle:
                    _mode = Mode.MoveFence;
                    return;
                case HitKind.FenceEdge:
                    _mode = Mode.ResizeFence;
                    return;
                case HitKind.FenceBody:
                    _mode = Mode.Lasso;
                    _lasso = Normalize(_down, p);
                    return;
                default:
                    _mode = InSwipeZone(_down) ? Mode.Swipe : Mode.Lasso;
                    _lasso = Normalize(_down, p);
                    return;
            }
        }
        if (_downButton == MouseButtons.Right && hit.Kind == HitKind.Nothing)
        {
            _mode = Mode.CreateFence;
            _createRect = Normalize(_down, p);
            return;
        }
        if (_downButton == MouseButtons.Middle)
        {
            _mode = Mode.Swipe;
            return;
        }
        _mode = Mode.None;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_quickHidden) return;
        var p = ToScreen(e);
        var mode = _mode;
        _mode = Mode.None;

        switch (mode)
        {
            case Mode.Pending:
                if (e.Button == MouseButtons.Right)
                {
                    switch (_downHit.Kind)
                    {
                        case HitKind.Item: ShowItemMenu(p); break;
                        case HitKind.FenceTitle: case HitKind.FenceEdge: ShowFenceMenu(_downHit.Fence!, p); break;
                        case HitKind.FenceBody: ShowFenceMenu(_downHit.Fence!, p); break;
                        default: ShowBackgroundMenu(p); break;
                    }
                }
                else if (e.Button == MouseButtons.Left && _downHit.Kind == HitKind.Item && !ModifierKeys.HasFlag(Keys.Control))
                {
                    // Click on an already-selected item without dragging selects only it.
                    _selection.Clear();
                    _selection.Add(_downHit.Item!.Value);
                }
                break;
            case Mode.MoveFence:
            case Mode.ResizeFence:
                EvictUnderFences();
                SaveSoon();
                break;
            case Mode.Lasso:
                SelectInLasso(Normalize(_down, p), ModifierKeys.HasFlag(Keys.Control));
                break;
            case Mode.CreateFence:
            {
                var rect = Normalize(_down, p);
                if (rect.Width >= 60 && rect.Height >= 40) ShowCreateFenceMenu(rect, p);
                break;
            }
            case Mode.Swipe:
            {
                int dx = p.X - _down.X;
                int threshold = Math.Max(40, _model.Settings.PageSwipeThreshold);
                if (dx <= -threshold) NextPage();
                else if (dx >= threshold) PreviousPage();
                break;
            }
        }
        _activeFence = null;
        RequestRender();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
        var p = ToScreen(e);
        if (_quickHidden)
        {
            if (_model.Settings.QuickHideOnDoubleClick) ToggleQuickHide();
            return;
        }
        var hit = HitTest(p);
        _mode = Mode.None;
        switch (hit.Kind)
        {
            case HitKind.Item:
            {
                var item = _model.Item(hit.Item!.Value);
                if (item != null) ShellActions.Open(item, Handle);
                break;
            }
            case HitKind.FenceTitle:
                hit.Fence!.RolledUp = !hit.Fence.RolledUp;
                hit.Fence.Peeking = false;
                if (!hit.Fence.RolledUp) EvictUnderFences();
                SaveSoon();
                RequestRender();
                break;
            case HitKind.Nothing:
                if (_model.Settings.QuickHideOnDoubleClick) ToggleQuickHide();
                break;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_quickHidden) return;
        var p = ToScreen(e);
        var hit = HitTest(p);
        if (hit.Fence == null || hit.Fence.IsCollapsed) return;
        var f = hit.Fence;
        var m = _renderer.MetricsAt(new Point(f.Bounds.X + f.Bounds.Width / 2, f.Bounds.Y + f.Bounds.Height / 2));
        var content = FenceLayoutEngine.ContentArea(f, m);
        int count = _model.ItemsOf(f).Count;
        int step = m.CellHeight * (e.Delta > 0 ? -1 : 1);
        f.ScrollOffset = FenceLayoutEngine.ClampScroll(f.ScrollOffset + step, count, content, m);
        RequestRender();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _peekOpenTimer.Stop();
        if (_model.Layout.ActivePage.Fences.Any(f => f.Peeking)) { _peekCloseTimer.Stop(); _peekCloseTimer.Start(); }
        if (_hover != null || _hoverFence != null)
        {
            _hover = null;
            _hoverFence = null;
            RequestRender();
        }
    }

    private static Rectangle Normalize(Point a, Point b)
        => Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    private void SelectInLasso(Rectangle lasso, bool add)
    {
        if (!add) _selection.Clear();
        var page = _model.Layout.ActivePage;
        foreach (var (key, pos) in page.FreeItems)
            if (_model.DesktopItems.ContainsKey(key) && FreeGrid.CellAt(pos, _renderer.MetricsAt(pos)).IntersectsWith(lasso))
                _selection.Add(new ItemRef(null, key));
        foreach (var f in page.Fences)
        {
            if (f.RolledUp) continue;
            var m = _renderer.MetricsAt(new Point(f.Bounds.X + f.Bounds.Width / 2, f.Bounds.Y + f.Bounds.Height / 2));
            var content = FenceLayoutEngine.ContentArea(f, m);
            if (!content.IntersectsWith(lasso)) continue;
            var items = _model.ItemsOf(f);
            var cells = FenceLayoutEngine.Arrange(items.Select(i => i.Key).ToList(), content, m, f.ScrollOffset);
            for (int i = 0; i < cells.Count; i++)
                if (cells[i].Visible && Rectangle.Intersect(cells[i].Bounds, content).IntersectsWith(lasso))
                    _selection.Add(new ItemRef(f.IsPortal ? f.Id : null, items[i].Key));
        }
    }

    // ---------------------------------------------------------------- keyboard

    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
            case Keys.Enter: case Keys.Delete: case Keys.F2: case Keys.Escape: case Keys.F5:
                return true;
        }
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_quickHidden) return;
        switch (e.KeyCode)
        {
            case Keys.Escape:
                _selection.Clear();
                RequestRender();
                break;
            case Keys.A when e.Control:
                foreach (var r in _model.VisibleRefs()) _selection.Add(r);
                RequestRender();
                break;
            case Keys.Enter:
            {
                if (e.Alt) { ShowProperties(); break; }
                foreach (var r in _selection.ToList())
                {
                    var item = _model.Item(r);
                    if (item != null) ShellActions.Open(item, Handle);
                }
                break;
            }
            case Keys.Delete:
                DeleteSelected(e.Shift);
                break;
            case Keys.F2:
                RenameSelected();
                break;
            case Keys.F5:
                _model.Icons.Clear();
                RefreshDesktop();
                break;
            case Keys.Left when e.Control:
                PreviousPage();
                break;
            case Keys.Right when e.Control:
                NextPage();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void DeleteSelected(bool permanent)
    {
        var paths = _selection.Select(_model.Item).Where(i => i != null && i.Path != null && i.CanDelete).Select(i => i!.Path!).ToList();
        if (paths.Count == 0) return;
        ShellActions.Delete(paths, Handle, permanent);
        ScheduleRefresh();
    }

    private void ShowProperties()
    {
        var items = _selection.Select(_model.Item).Where(i => i != null).Select(i => i!).ToList();
        if (items.Count == 0) return;
        var first = items[0];
        ShellActions.InvokeVerb(items.Where(i => ReferenceEquals(i.Parent, first.Parent)).ToList(), "properties", Handle);
    }

    private void RenameSelected()
    {
        if (_selection.Count != 1) return;
        var r = _selection.First();
        var item = _model.Item(r);
        if (item == null || !item.CanRename) return;
        var cell = FindCell(r);
        if (cell == Rectangle.Empty) return;
        var m = _renderer.MetricsAt(cell.Location);
        var label = _renderer.LabelRect(cell, m);
        double scale = m.CellWidth / (double)Math.Max(1, _model.Settings.CellWidth);
        var font = new Font(_model.Settings.LabelFont, (float)(_model.Settings.LabelFontSize * scale), GraphicsUnit.Point);
        PopupEditor.Show(label, item.DisplayName, font, multiline: true, newName =>
        {
            font.Dispose();
            if (string.IsNullOrWhiteSpace(newName) || newName == item.DisplayName) return;
            RememberPlacementAfterRename(r, item, newName);
            if (!ShellActions.Rename(item, newName, Handle)) _model.Pending.Clear();
            ScheduleRefresh();
        });
    }

    /// <summary>After a rename the key changes, so remember where the item was.</summary>
    private void RememberPlacementAfterRename(ItemRef r, ShellItemInfo item, string newName)
    {
        if (r.PortalId != null || item.Path == null) return;
        var dir = Path.GetDirectoryName(item.Path) ?? "";
        var ext = Path.GetExtension(item.Path);
        var fileName = Path.GetFileName(item.Path);
        bool extHidden = !item.IsFolder && ext.Length > 0 && !fileName.Equals(item.DisplayName, StringComparison.OrdinalIgnoreCase);
        var expected = Path.Combine(dir, extHidden && !newName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? newName + ext : newName);
        var page = _model.Layout.ActivePage;
        var fence = page.FenceContaining(r.Key);
        _model.Pending[expected] = fence != null
            ? new PendingPlacement(fence.Id, null)
            : new PendingPlacement(null, page.FreeItems.TryGetValue(r.Key, out var pos) ? pos : null);
    }

    private Rectangle FindCell(ItemRef r)
    {
        var page = _model.Layout.ActivePage;
        if (r.PortalId == null && page.FreeItems.TryGetValue(r.Key, out var pos))
            return FreeGrid.CellAt(pos, _renderer.MetricsAt(pos));
        foreach (var f in page.Fences)
        {
            if (f.RolledUp) continue;
            if (r.PortalId != null ? f.Id != r.PortalId : f.IsPortal) continue;
            var items = _model.ItemsOf(f);
            int idx = items.FindIndex(i => string.Equals(i.Key, r.Key, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) continue;
            var m = _renderer.MetricsAt(new Point(f.Bounds.X + f.Bounds.Width / 2, f.Bounds.Y + f.Bounds.Height / 2));
            var content = FenceLayoutEngine.ContentArea(f, m);
            var cells = FenceLayoutEngine.Arrange(items.Select(i => i.Key).ToList(), content, m, f.ScrollOffset);
            return cells[idx].Bounds;
        }
        return Rectangle.Empty;
    }

    // ---------------------------------------------------------------- context menus

    private void ShowItemMenu(Point screenPt)
    {
        var items = _selection.Select(_model.Item).Where(i => i != null).Select(i => i!).ToList();
        if (items.Count == 0) return;
        var primary = _downHit.Item is { } r ? _model.Item(r) : items[0];
        if (primary == null) return;
        items = items.Where(i => ReferenceEquals(i.Parent, primary.Parent)).ToList();
        try
        {
            using var menu = ShellActions.CreateItemMenu(items, Handle);
            menu.Populate();
            _shellMenu = menu;
            uint cmd;
            try { cmd = menu.Show(Handle, screenPt); }
            finally { _shellMenu = null; }
            if (cmd == 0) return;
            var verb = menu.GetVerb(cmd);
            if (string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase)) { RenameSelected(); return; }
            menu.Invoke(cmd, Handle, screenPt);
            ScheduleRefresh();
        }
        catch (Exception ex)
        {
            Log.Error("Item menu failed", ex);
        }
    }

    private void ShowBackgroundMenu(Point screenPt)
    {
        try
        {
            using var menu = ShellActions.CreateBackgroundMenu(ShellEnumerator.DesktopFolder, Handle);
            menu.Populate(Const.CMF_NORMAL | Const.CMF_EXPLORE);

            int pos = 0;
            menu.AddOwnItem(IdNewFence, "New Orgo here", pos++);
            menu.AddOwnItem(IdNewPortal, "New folder portal here...", pos++);
            menu.AddSeparator(pos++);
            {
                var presets = menu.AddOwnSubmenu("Presets", pos++);
                var list = _model.Layout.Presets;
                var active = Presets.Active(_model.Layout);
                for (int i = 0; i < list.Count; i++)
                    ContextMenuSession.AddSubItem(presets, IdPresetBase + (uint)i, list[i].Name, list[i] == active);
                if (list.Count > 0) ContextMenuSession.AddSubSeparator(presets);
                ContextMenuSession.AddSubItem(presets, IdSavePreset, "Save current desktop as preset...");
                ContextMenuSession.AddSubItem(presets, IdUpdatePreset, active != null ? $"Update \"{active.Name}\" from current desktop" : "Update preset from current desktop", enabled: active != null);
                if (list.Count > 0)
                {
                    var del = User32.CreatePopupMenu();
                    for (int i = 0; i < list.Count; i++) ContextMenuSession.AddSubItem(del, IdDeletePresetBase + (uint)i, list[i].Name);
                    User32.AppendMenu(presets, Const.MF_POPUP | Const.MF_STRING, (UIntPtr)(ulong)del, "Delete preset");
                }
            }
            if (_model.Settings.EnablePages)
            {
                var pages = menu.AddOwnSubmenu("Desktop pages", pos++);
                for (int i = 0; i < _model.Layout.Pages.Count; i++)
                    ContextMenuSession.AddSubItem(pages, IdPageBase + (uint)i, _model.Layout.Pages[i].Name, i == _model.Layout.ActivePageIndex);
                ContextMenuSession.AddSubSeparator(pages);
                ContextMenuSession.AddSubItem(pages, IdNewPage, "New page...");
                ContextMenuSession.AddSubItem(pages, IdRenamePage, "Rename this page...");
                ContextMenuSession.AddSubItem(pages, IdDeletePage, "Delete this page", enabled: _model.Layout.Pages.Count > 1);
            }
            menu.AddOwnItem(IdSortRules, "Sort icons by rules now", pos++);
            menu.AddOwnItem(IdQuickHide, "Hide icons (double-click desktop to show)", pos++, isChecked: _quickHidden);
            menu.AddOwnItem(IdRefresh, "Refresh icons", pos++);
            menu.AddSeparator(pos++);
            menu.AddOwnItem(IdSnapshots, "Layout snapshots...", pos++);
            menu.AddOwnItem(IdSettings, "Orgodesk settings...", pos++);
            menu.AddSeparator(pos++);
            menu.AddSeparator();
            menu.AddOwnItem(IdSuspend, "Switch Orgodesk off (normal desktop; double right-click to switch back on)");
            menu.AddOwnItem(IdExit, "Exit Orgodesk (restore desktop icons)");

            _shellMenu = menu;
            uint cmd;
            try { cmd = menu.Show(Handle, screenPt); }
            finally { _shellMenu = null; }
            if (cmd == 0) return;
            if (ContextMenuSession.IsShellCommand(cmd))
            {
                menu.Invoke(cmd, Handle, screenPt);
                ScheduleRefresh();
                return;
            }
            HandleOwnCommand(cmd, screenPt);
        }
        catch (Exception ex)
        {
            Log.Error("Background menu failed", ex);
        }
    }

    private void HandleOwnCommand(uint cmd, Point screenPt)
    {
        switch (cmd)
        {
            case IdNewFence:
            {
                var m = _renderer.MetricsAt(screenPt);
                CreateFence(new Rectangle(screenPt.X, screenPt.Y, m.CellWidth * 3 + 2 * m.Padding, m.TitleBarHeight + m.CellHeight * 2 + 2 * m.Padding));
                break;
            }
            case IdNewPortal:
            {
                using var dlg = new FolderBrowserDialog { Description = "Choose the folder this portal shows", UseDescriptionForTitle = true };
                if (dlg.ShowDialog() != DialogResult.OK) break;
                var m = _renderer.MetricsAt(screenPt);
                CreateFence(new Rectangle(screenPt.X, screenPt.Y, m.CellWidth * 4 + 2 * m.Padding, m.TitleBarHeight + m.CellHeight * 3 + 2 * m.Padding), null, dlg.SelectedPath);
                break;
            }
            case IdSortRules: ApplyRulesNow(); break;
            case IdQuickHide: ToggleQuickHide(); break;
            case IdRefresh: _model.Icons.Clear(); RefreshDesktop(); break;
            case IdSnapshots: SnapshotsRequested?.Invoke(this, EventArgs.Empty); break;
            case IdSettings: SettingsRequested?.Invoke(this, EventArgs.Empty); break;
            case IdExit: ExitRequested?.Invoke(this, EventArgs.Empty); break;
            case IdSuspend: Suspend(); break;
            case IdSavePreset: SavePreset(); break;
            case IdUpdatePreset: UpdateActivePreset(); break;
            case IdNewPage: AddPage(); break;
            case IdRenamePage: RenamePage(); break;
            case IdDeletePage: DeletePage(); break;
            default:
                if (cmd >= IdDeletePresetBase && cmd < IdDeletePresetBase + 200) DeletePreset((int)(cmd - IdDeletePresetBase));
                else if (cmd >= IdPresetBase && cmd < IdPresetBase + 200) ApplyPreset((int)(cmd - IdPresetBase));
                else if (cmd >= IdPageBase && cmd < IdPageBase + 100) GoToPage((int)(cmd - IdPageBase));
                break;
        }
    }

    private void ShowFenceMenu(Fence fence, Point screenPt)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Rename Orgo", null, (_, _) => RenameFence(fence));
        menu.Items.Add("Orgo properties...", null, (_, _) => EditFence(fence));
        var sort = new ToolStripMenuItem("Sort items by");
        foreach (SortMode mode in Enum.GetValues<SortMode>())
        {
            if (fence.IsPortal && mode == SortMode.Manual) continue;
            var mi = new ToolStripMenuItem(mode.ToString()) { Checked = fence.Sort == mode };
            mi.Click += (_, _) => { fence.Sort = mode; SaveSoon(); RequestRender(); };
            sort.DropDownItems.Add(mi);
        }
        menu.Items.Add(sort);
        menu.Items.Add(fence.RolledUp ? "Unroll" : "Roll up", null, (_, _) => { fence.RolledUp = !fence.RolledUp; fence.Peeking = false; if (!fence.RolledUp) EvictUnderFences(); SaveSoon(); RequestRender(); });
        var auto = new ToolStripMenuItem("Roll up automatically (open on hover)") { Checked = fence.AutoRollUp };
        auto.Click += (_, _) =>
        {
            fence.AutoRollUp = !fence.AutoRollUp;
            fence.Peeking = false;
            if (fence.AutoRollUp) fence.RolledUp = true;
            SaveSoon();
            RequestRender();
        };
        menu.Items.Add(auto);
        if (fence.IsPortal)
            menu.Items.Add("Open folder", null, (_, _) => ShellActions.OpenPath(fence.PortalPath!, Handle));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(fence.IsPortal ? "Remove portal" : "Remove Orgo (icons go back to the desktop)", null, (_, _) => DeleteFence(fence));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Orgodesk settings...", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        // Dispose later, never inside Closed: WinForms' menu filter still touches the strip on the next activation change.
        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));
        menu.Show(screenPt);
    }

    private void ShowCreateFenceMenu(Rectangle rect, Point screenPt)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Create Orgo here", null, (_, _) => CreateFence(rect));
        menu.Items.Add("Create folder portal here...", null, (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Choose the folder this portal shows", UseDescriptionForTitle = true };
            if (dlg.ShowDialog() == DialogResult.OK) CreateFence(rect, null, dlg.SelectedPath);
        });
        menu.Items.Add("Cancel");
        menu.Closed += (_, _) => { BeginInvoke(new Action(menu.Dispose)); RequestRender(); };
        _createRect = rect;
        menu.Show(screenPt);
    }

    public void RenameFence(Fence fence)
    {
        var m = _renderer.MetricsAt(fence.Bounds.Location);
        var bar = FenceLayoutEngine.TitleBar(fence, m);
        bar.Inflate(-m.Padding, -2);
        double scale = m.CellWidth / (double)Math.Max(1, _model.Settings.CellWidth);
        var font = new Font(_model.Settings.LabelFont, (float)(_model.Settings.TitleFontSize * scale), FontStyle.Bold, GraphicsUnit.Point);
        PopupEditor.Show(bar, fence.Title, font, multiline: false, text =>
        {
            font.Dispose();
            if (string.IsNullOrWhiteSpace(text)) return;
            fence.Title = text.Trim();
            SaveSoon();
            RequestRender();
        });
    }

    public void EditFence(Fence fence)
    {
        using var dlg = new FencePropertiesForm(fence, _model.Settings);
        if (dlg.ShowDialog() != DialogResult.OK) return;
        bool portalChanged = dlg.Apply(fence);
        EvictUnderFences();
        if (portalChanged) { _model.RefreshPortals(); _watcher?.SetWatchedFolders(_model.PortalPaths()); }
        SaveSoon();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        RequestRender();
    }

    // ---------------------------------------------------------------- OLE drag and drop

    private void BeginItemDrag(Hit hit)
    {
        var primary = hit.Item!.Value;
        var refs = (_selection.Contains(primary) ? _selection : new HashSet<ItemRef> { primary })
            .Where(r => r.PortalId == primary.PortalId).ToList();
        var items = refs.Select(_model.Item).Where(i => i != null).Select(i => i!).ToList();
        if (items.Count == 0) return;
        var data = ShellActions.CreateDataObject(items, Handle);
        if (data == null) return;

        _dragging = new HashSet<ItemRef>(refs);
        _dragGrabOffset = new Point(_down.X - hit.Cell.X, _down.Y - hit.Cell.Y);
        _droppedHere = false;
        RequestRender();
        DragDropEffects effect;
        try { effect = DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
        catch (Exception ex) { Log.Error("Drag failed", ex); effect = DragDropEffects.None; }
        _dragging = null;
        _drop = null;
        _mode = Mode.None;
        if (!_droppedHere && effect != DragDropEffects.None) ScheduleRefresh();
        RequestRender();
    }

    private bool IsExternalFileDrag(DragEventArgs e) => _dragging == null && e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop);

    private DropTarget ComputeDrop(Point p, DragEventArgs e)
    {
        bool internalDesktop = _dragging != null && _dragging.All(r => r.PortalId == null);
        bool internalPortal = _dragging != null && !internalDesktop;
        bool external = IsExternalFileDrag(e);
        if (!internalDesktop && !internalPortal && !external) return new DropTarget(null, 0, null, false);

        var page = _model.Layout.ActivePage;
        for (int i = page.Fences.Count - 1; i >= 0; i--)
        {
            var f = page.Fences[i];
            var m = _renderer.MetricsAt(new Point(f.Bounds.X + f.Bounds.Width / 2, f.Bounds.Y + f.Bounds.Height / 2));
            if (!FenceLayoutEngine.VisibleBounds(f, m).Contains(p)) continue;
            if (f.IsCollapsed) return new DropTarget(f, 0, null, false);
            if (f.IsPortal)
            {
                bool samePortal = internalPortal && _dragging!.All(r => r.PortalId == f.Id);
                bool hasFiles = external || (_dragging?.All(r => _model.Item(r)?.Path != null) ?? false);
                return new DropTarget(f, 0, null, hasFiles && !samePortal);
            }
            var content = FenceLayoutEngine.ContentArea(f, m);
            int count = _model.ItemsOf(f).Count;
            int idx = FenceLayoutEngine.InsertionIndex(p, count, content, m, f.ScrollOffset);
            bool ok = internalDesktop || external || (_dragging?.All(r => _model.Item(r)?.Path != null) ?? false);
            return new DropTarget(f, idx, null, ok && f.Sort == SortMode.Manual || ok);
        }

        var pos = internalDesktop ? new Point(p.X - _dragGrabOffset.X, p.Y - _dragGrabOffset.Y) : p;
        var mm = _renderer.MetricsAt(p);
        if (!internalDesktop) pos = new Point(p.X - mm.CellWidth / 2, p.Y - mm.CellHeight / 2);
        var area = ScreenInfo.WorkAreaAt(p);
        if (_model.Settings.SnapFreeIconsToGrid) pos = FreeGrid.Snap(pos, area, mm);
        bool valid = internalDesktop || external || (_dragging?.All(r => _model.Item(r)?.Path != null) ?? false);
        return new DropTarget(null, 0, pos, valid);
    }

    private DragDropEffects EffectFor(DropTarget t, DragEventArgs e)
    {
        if (!t.Valid) return DragDropEffects.None;
        bool ctrl = (e.KeyState & 8) != 0;
        if (_dragging != null) return DragDropEffects.Move;
        if (ctrl) return DragDropEffects.Copy;
        var paths = e.Data?.GetData(DataFormats.FileDrop) as string[];
        string dest = t.Fence?.PortalPath ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        bool sameVolume = paths != null && paths.All(p => ShellActions.IsSameVolume(p, dest));
        return sameVolume ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        _isDragOver = true;
        OnDragOver(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        var p = new Point(e.X, e.Y);
        UpdatePeek(FenceAt(p), p);
        var target = ComputeDrop(p, e);
        e.Effect = EffectFor(target, e) & e.AllowedEffect;
        if (e.Effect == DragDropEffects.None && target.Valid) e.Effect = e.AllowedEffect.HasFlag(DragDropEffects.Move) ? DragDropEffects.Move : e.AllowedEffect.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.None;
        if (!Equals(target, _drop))
        {
            _drop = target;
            RequestRender();
        }
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        _isDragOver = false;
        _drop = null;
        _peekCloseTimer.Stop();
        _peekCloseTimer.Start();
        RequestRender();
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        _isDragOver = false;
        var p = new Point(e.X, e.Y);
        var target = ComputeDrop(p, e);
        _drop = null;
        if (!target.Valid) { e.Effect = DragDropEffects.None; RequestRender(); return; }
        e.Effect = EffectFor(target, e);
        try
        {
            if (_dragging != null) DropInternal(target, e);
            else DropExternal(target, e);
        }
        catch (Exception ex)
        {
            Log.Error("Drop failed", ex);
        }
        RequestRender();
    }

    private void DropInternal(DropTarget target, DragEventArgs e)
    {
        _droppedHere = true;
        var refs = _dragging!.ToList();
        var page = _model.Layout.ActivePage;
        bool desktopRefs = refs.All(r => r.PortalId == null);
        string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        if (!desktopRefs)
        {
            // Files inside a portal: a real move.
            var paths = refs.Select(_model.Item).Where(i => i?.Path != null).Select(i => i!.Path!).ToList();
            if (target.Fence is { IsPortal: true } portal)
            {
                ShellActions.Transfer(paths, portal.PortalPath!, copy: false, Handle);
            }
            else
            {
                foreach (var path in paths)
                    _model.Pending[Path.Combine(desktopDir, Path.GetFileName(path))] = new PendingPlacement(target.Fence?.Id, target.FreePosition);
                ShellActions.Transfer(paths, desktopDir, copy: false, Handle);
            }
            ScheduleRefresh();
            return;
        }

        if (target.Fence is { IsPortal: true } portalTarget)
        {
            var paths = refs.Select(_model.Item).Where(i => i?.Path != null).Select(i => i!.Path!).ToList();
            ShellActions.Transfer(paths, portalTarget.PortalPath!, copy: false, Handle);
            ScheduleRefresh();
            return;
        }

        if (target.Fence != null)
        {
            var f = target.Fence;
            var before = _model.ItemsOf(f).Select(i => i.Key).ToList();
            int idx = target.InsertIndex;
            // Removing the dragged items shifts the insertion point left by however many sat before it.
            idx -= before.Take(idx).Count(k => refs.Any(r => string.Equals(r.Key, k, StringComparison.OrdinalIgnoreCase)));
            foreach (var r in refs) page.Remove(r.Key);
            if (f.Sort != SortMode.Manual)
            {
                foreach (var r in refs) f.Items.Add(r.Key);
            }
            else
            {
                // Keep the order the items had on screen.
                var ordered = refs.OrderBy(r => before.FindIndex(k => k.Equals(r.Key, StringComparison.OrdinalIgnoreCase)) is var i and >= 0 ? i : int.MaxValue).ToList();
                idx = Math.Clamp(idx, 0, f.Items.Count);
                foreach (var r in ordered) f.Items.Insert(idx++, r.Key);
            }
        }
        else if (target.FreePosition is { } pos)
        {
            var primary = refs.FirstOrDefault(r => page.FreeItems.ContainsKey(r.Key));
            Point? primaryOld = primary != default && page.FreeItems.TryGetValue(primary.Key, out var po) ? po : null;
            var area = ScreenInfo.WorkAreaAt(pos);
            var m = _renderer.MetricsAt(pos);
            var placed = new List<Rectangle>();
            foreach (var r in refs)
            {
                Point? old = page.FreeItems.TryGetValue(r.Key, out var o) ? o : null;
                page.Remove(r.Key);
                Point np;
                if (r == primary) np = pos;
                else if (old != null && primaryOld != null) np = new Point(pos.X + (old.Value.X - primaryOld.Value.X), pos.Y + (old.Value.Y - primaryOld.Value.Y));
                else np = FreeGrid.NextFreeSlot(area, m,
                    page.FreeItems.Values.Select(v => FreeGrid.CellAt(v, m)).Concat(placed),
                    page.Fences.Select(x => FenceLayoutEngine.VisibleBounds(x, m)));
                if (_model.Settings.SnapFreeIconsToGrid) np = FreeGrid.Snap(np, area, m);
                page.FreeItems[r.Key] = np;
                placed.Add(FreeGrid.CellAt(np, m));
            }
        }
        SaveSoon();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DropExternal(DropTarget target, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        bool copy = e.Effect == DragDropEffects.Copy;
        if (target.Fence is { IsPortal: true } portal)
        {
            ShellActions.Transfer(paths, portal.PortalPath!, copy, Handle);
        }
        else
        {
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var m = _renderer.MetricsAt(target.FreePosition ?? ScreenInfo.PrimaryWorkArea.Location);
            int i = 0;
            foreach (var path in paths)
            {
                Point? pos = target.FreePosition is { } fp ? new Point(fp.X, fp.Y + (i++) * m.CellHeight) : null;
                _model.Pending[Path.Combine(desktopDir, Path.GetFileName(path))] = new PendingPlacement(target.Fence?.Id, pos);
            }
            ShellActions.Transfer(paths, desktopDir, copy, Handle);
        }
        ScheduleRefresh();
    }

    // ---------------------------------------------------------------- shutdown

    public void Shutdown()
    {
        _closing = true;
        _watchdog.Stop();
        _saveTimer.Stop();
        _refreshTimer.Stop();
        _hudTimer.Stop();
        _peekOpenTimer.Stop();
        _peekCloseTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SaveNow();
        _watcher?.Dispose();
        _watcher = null;
        if (IsHandleCreated) DestroyHandle();
        _dib?.Dispose();
        _dib = null;
        _renderer.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing) Shutdown();
        base.Dispose(disposing);
    }
}
