using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using Orgodesk.Core.Persistence;
using Orgodesk.Core.Rules;
using Orgodesk.Windows.Desktop;

namespace Orgodesk.App.UI;

/// <summary>Appearance, behavior and auto-sort rules. Non-modal; Apply pushes changes live.</summary>
public sealed class SettingsForm : Form
{
    private readonly DesktopModel _model;
    private readonly LayoutStore _store;
    private readonly DesktopSurface _surface;

    // Appearance
    private readonly ColorPicker _fenceColor = new();
    private readonly ColorPicker _titleColor = new();
    private readonly ColorPicker _labelColor = new();
    private readonly ColorPicker _selectionColor = new();
    private readonly ComboBox _font = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly NumericUpDown _labelSize = new() { Minimum = 6, Maximum = 24, DecimalPlaces = 1, Increment = 0.5m, Width = 70 };
    private readonly NumericUpDown _titleSize = new() { Minimum = 6, Maximum = 28, DecimalPlaces = 1, Increment = 0.5m, Width = 70 };
    private readonly NumericUpDown _radius = new() { Minimum = 0, Maximum = 30, Width = 70 };
    private readonly ComboBox _iconSize = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly NumericUpDown _cellW = new() { Minimum = 48, Maximum = 300, Width = 70 };
    private readonly NumericUpDown _cellH = new() { Minimum = 48, Maximum = 300, Width = 70 };
    private readonly NumericUpDown _titleH = new() { Minimum = 16, Maximum = 60, Width = 70 };
    private readonly CheckBox _shadow = new() { Text = "Label shadow", AutoSize = true };

    // Behavior
    private readonly CheckBox _quickHide = new() { Text = "Double-click empty desktop to hide / show icons", AutoSize = true };
    private readonly CheckBox _rightToggle = new() { Text = "Double right-click the desktop to switch the clone off and on (normal desktop while off)", AutoSize = true };
    private readonly CheckBox _snap = new() { Text = "Snap free icons to the grid", AutoSize = true };
    private readonly CheckBox _snapFences = new() { Text = "Snap Orgos to each other and to the screen edges", AutoSize = true };
    private readonly CheckBox _pages = new() { Text = "Enable desktop pages as well as presets (middle-drag or edge-drag to swipe, Ctrl+Left/Right)", AutoSize = true };
    private readonly NumericUpDown _swipe = new() { Minimum = 40, Maximum = 600, Width = 70 };
    private readonly CheckBox _dots = new() { Text = "Show page indicator dots", AutoSize = true };
    private readonly CheckBox _autostart = new() { Text = "Start with Windows", AutoSize = true };

    // Rules
    private readonly ListView _rules = new() { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill, CheckBoxes = true };
    private List<Rule> _ruleList = new();

    public SettingsForm(DesktopModel model, LayoutStore store, DesktopSurface surface)
    {
        _model = model;
        _store = store;
        _surface = surface;

        Text = "Orgodesk settings";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 520);
        MinimumSize = new Size(560, 460);
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildAppearanceTab());
        tabs.TabPages.Add(BuildBehaviorTab());
        tabs.TabPages.Add(BuildRulesTab());

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
        var cancel = new Button { Text = "Close", Width = 90 };
        var apply = new Button { Text = "Apply", Width = 90 };
        var ok = new Button { Text = "OK", Width = 90 };
        cancel.Click += (_, _) => Close();
        apply.Click += (_, _) => Apply();
        ok.Click += (_, _) => { Apply(); Close(); };
        buttons.Controls.AddRange(new Control[] { cancel, apply, ok });
        AcceptButton = ok;

        Controls.Add(tabs);
        Controls.Add(buttons);
        Load += (_, _) => LoadValues();
    }

    private static TableLayoutPanel Grid()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(10) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    private static void Row(TableLayoutPanel t, string label, Control c)
    {
        int r = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 5, 0, 0) }, 0, r);
        c.Anchor = AnchorStyles.Left;
        c.Margin = new Padding(3, 4, 3, 4);
        t.Controls.Add(c, 1, r);
    }

    private TabPage BuildAppearanceTab()
    {
        var page = new TabPage("Appearance") { AutoScroll = true };
        var t = Grid();
        Row(t, "Orgo color", _fenceColor);
        Row(t, "Title bar color", _titleColor);
        Row(t, "Label color", _labelColor);
        Row(t, "Selection color", _selectionColor);
        using (var fonts = new InstalledFontCollection())
            foreach (var f in fonts.Families) _font.Items.Add(f.Name);
        Row(t, "Font", _font);
        Row(t, "Label size (pt)", _labelSize);
        Row(t, "Title size (pt)", _titleSize);
        foreach (var s in new[] { 32, 48, 64, 96, 128 }) _iconSize.Items.Add(s);
        Row(t, "Icon size (px at 100%)", _iconSize);
        Row(t, "Grid cell width", _cellW);
        Row(t, "Grid cell height", _cellH);
        Row(t, "Title bar height", _titleH);
        Row(t, "Corner radius", _radius);
        Row(t, "", _shadow);
        page.Controls.Add(t);
        return page;
    }

    private TabPage BuildBehaviorTab()
    {
        var page = new TabPage("Behavior") { AutoScroll = true };
        var t = Grid();
        Row(t, "", _quickHide);
        Row(t, "", _rightToggle);
        Row(t, "", _snap);
        Row(t, "", _snapFences);
        Row(t, "", _pages);
        Row(t, "Swipe distance (px)", _swipe);
        Row(t, "", _dots);
        Row(t, "", _autostart);
        var help = new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0), Padding = new Padding(10),
            Text = "Tips: right-drag on empty desktop to draw a new Orgo. Double-click an Orgo title to roll it up. " +
                   "Drag icons between Orgos and the desktop. F2 renames, Delete recycles, Alt+Enter shows properties. " +
                   "Mouse wheel scrolls a full Orgo. Exit from the tray icon to bring the normal desktop icons back.",
        };
        page.Controls.Add(help);
        page.Controls.Add(t);
        return page;
    }

    private TabPage BuildRulesTab()
    {
        var page = new TabPage("Auto-sort rules");
        _rules.Columns.Add("On", 36);
        _rules.Columns.Add("Name", 130);
        _rules.Columns.Add("Kind", 90);
        _rules.Columns.Add("Pattern", 150);
        _rules.Columns.Add("Type", 70);
        _rules.Columns.Add("Target Orgo", 140);
        _rules.ItemChecked += (_, e) =>
        {
            if (e.Item.Tag is Rule r) r.Enabled = e.Item.Checked;
        };
        _rules.DoubleClick += (_, _) => EditRule();

        var side = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.TopDown, Width = 120, Padding = new Padding(6) };
        Button B(string text, EventHandler h) { var b = new Button { Text = text, Width = 100 }; b.Click += h; return b; }
        side.Controls.Add(B("Add...", (_, _) => AddRule()));
        side.Controls.Add(B("Edit...", (_, _) => EditRule()));
        side.Controls.Add(B("Remove", (_, _) => RemoveRule()));
        side.Controls.Add(B("Move up", (_, _) => MoveRule(-1)));
        side.Controls.Add(B("Move down", (_, _) => MoveRule(1)));
        side.Controls.Add(new Label { Height = 12 });
        side.Controls.Add(B("Apply rules now", (_, _) => { SaveRules(); _surface.ApplyRulesNow(); }));
        var hint = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = 40, Padding = new Padding(6),
            Text = "Rules run top to bottom; the first match wins. New desktop items are sorted when they appear. \"Apply rules now\" sorts everything.",
        };
        page.Controls.Add(_rules);
        page.Controls.Add(side);
        page.Controls.Add(hint);
        return page;
    }

    private void LoadValues()
    {
        var s = _model.Settings;
        _fenceColor.Value = s.FenceColor;
        _titleColor.Value = s.TitleBarColor;
        _labelColor.Value = s.LabelColor;
        _selectionColor.Value = s.SelectionColor;
        _font.SelectedItem = _font.Items.Contains(s.LabelFont) ? s.LabelFont : "Segoe UI";
        _labelSize.Value = (decimal)Math.Clamp(s.LabelFontSize, 6, 24);
        _titleSize.Value = (decimal)Math.Clamp(s.TitleFontSize, 6, 28);
        _radius.Value = Math.Clamp(s.CornerRadius, 0, 30);
        _iconSize.SelectedItem = _iconSize.Items.Contains(s.IconSize) ? s.IconSize : 48;
        _cellW.Value = Math.Clamp(s.CellWidth, 48, 300);
        _cellH.Value = Math.Clamp(s.CellHeight, 48, 300);
        _titleH.Value = Math.Clamp(s.TitleBarHeight, 16, 60);
        _shadow.Checked = s.LabelShadow;
        _quickHide.Checked = s.QuickHideOnDoubleClick;
        _rightToggle.Checked = s.ToggleOnDoubleRightClick;
        _snap.Checked = s.SnapFreeIconsToGrid;
        _snapFences.Checked = s.SnapFences;
        _pages.Checked = s.EnablePages;
        _swipe.Value = Math.Clamp(s.PageSwipeThreshold, 40, 600);
        _dots.Checked = s.ShowPageIndicator;
        _autostart.Checked = s.StartWithWindows;
        _ruleList = _model.Layout.Rules.Select(r => r.Clone()).ToList();
        RefreshFenceList();
    }

    /// <summary>Redraws the rules list (fence titles may have changed).</summary>
    public void RefreshFenceList()
    {
        if (IsDisposed) return;
        _rules.BeginUpdate();
        _rules.Items.Clear();
        foreach (var r in _ruleList)
        {
            var fence = _model.Layout.FindFence(r.TargetFenceId);
            var li = new ListViewItem("") { Checked = r.Enabled, Tag = r };
            li.SubItems.Add(r.Name);
            li.SubItems.Add(r.Kind.ToString());
            li.SubItems.Add(r.Kind == RuleKind.ItemType ? "" : r.Pattern);
            li.SubItems.Add(r.ItemType.ToString());
            li.SubItems.Add(fence?.Title ?? "(missing Orgo)");
            _rules.Items.Add(li);
        }
        _rules.EndUpdate();
    }

    private void AddRule()
    {
        using var dlg = new RuleEditForm(new Rule(), _model.Layout);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _ruleList.Add(dlg.Result);
        RefreshFenceList();
    }

    private void EditRule()
    {
        if (_rules.SelectedItems.Count == 0 || _rules.SelectedItems[0].Tag is not Rule r) return;
        using var dlg = new RuleEditForm(r, _model.Layout);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        int idx = _ruleList.IndexOf(r);
        _ruleList[idx] = dlg.Result;
        RefreshFenceList();
        _rules.Items[idx].Selected = true;
    }

    private void RemoveRule()
    {
        if (_rules.SelectedItems.Count == 0 || _rules.SelectedItems[0].Tag is not Rule r) return;
        _ruleList.Remove(r);
        RefreshFenceList();
    }

    private void MoveRule(int delta)
    {
        if (_rules.SelectedItems.Count == 0 || _rules.SelectedItems[0].Tag is not Rule r) return;
        int idx = _ruleList.IndexOf(r), to = idx + delta;
        if (to < 0 || to >= _ruleList.Count) return;
        _ruleList.RemoveAt(idx);
        _ruleList.Insert(to, r);
        RefreshFenceList();
        _rules.Items[to].Selected = true;
    }

    private void SaveRules()
    {
        _model.Layout.Rules = _ruleList.Select(r => r.Clone()).ToList();
        _surface.SaveSoon();
    }

    private void Apply()
    {
        var s = _model.Settings;
        s.FenceColor = _fenceColor.Value;
        s.TitleBarColor = _titleColor.Value;
        s.LabelColor = _labelColor.Value;
        s.SelectionColor = _selectionColor.Value;
        s.LabelFont = _font.SelectedItem as string ?? "Segoe UI";
        s.LabelFontSize = (float)_labelSize.Value;
        s.TitleFontSize = (float)_titleSize.Value;
        s.CornerRadius = (int)_radius.Value;
        s.IconSize = _iconSize.SelectedItem is int i ? i : 48;
        s.CellWidth = (int)_cellW.Value;
        s.CellHeight = (int)_cellH.Value;
        s.TitleBarHeight = (int)_titleH.Value;
        s.LabelShadow = _shadow.Checked;
        s.QuickHideOnDoubleClick = _quickHide.Checked;
        s.ToggleOnDoubleRightClick = _rightToggle.Checked;
        s.SnapFreeIconsToGrid = _snap.Checked;
        s.SnapFences = _snapFences.Checked;
        s.EnablePages = _pages.Checked;
        s.PageSwipeThreshold = (int)_swipe.Value;
        s.ShowPageIndicator = _dots.Checked;
        s.StartWithWindows = _autostart.Checked;
        try { _store.SaveSettings(s); } catch (Exception ex) { Log.Error("Save settings failed", ex); }
        try { AutoStart.SetEnabled(s.StartWithWindows, Application.ExecutablePath); } catch (Exception ex) { Log.Error("Autostart failed", ex); }
        SaveRules();
        _surface.ApplySettings();
    }
}
