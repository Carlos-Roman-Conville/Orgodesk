using System.Drawing;
using System.Windows.Forms;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Persistence;

namespace Orgodesk.App.UI;

/// <summary>Title, color, portal folder and sort mode of one fence.</summary>
public sealed class FencePropertiesForm : Form
{
    private readonly TextBox _title = new() { Width = 320 };
    private readonly CheckBox _customColor = new() { Text = "Use custom colors for this Orgo", AutoSize = true };
    private readonly ColorPicker _color = new();
    private readonly ColorPicker _titleColor = new();
    private readonly TextBox _portal = new() { Width = 240 };
    private readonly ComboBox _sort = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly CheckBox _rolled = new() { Text = "Rolled up", AutoSize = true };
    private readonly CheckBox _autoRoll = new() { Text = "Roll up automatically (opens while the mouse is over it)", AutoSize = true };
    private readonly string? _originalPortal;

    public FencePropertiesForm(Fence fence, AppSettings settings)
    {
        _originalPortal = fence.PortalPath;
        Text = "Orgo properties";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(500, 300);

        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, Control c)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, 0, r);
            c.Margin = new Padding(3, 4, 3, 4);
            t.Controls.Add(c, 1, r);
        }

        var portalRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var browse = new Button { Text = "Browse...", Width = 80 };
        var clear = new Button { Text = "Clear", Width = 60 };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Folder this Orgo shows", UseDescriptionForTitle = true, SelectedPath = _portal.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK) _portal.Text = dlg.SelectedPath;
        };
        clear.Click += (_, _) => _portal.Text = "";
        portalRow.Controls.AddRange(new Control[] { _portal, browse, clear });

        foreach (SortMode m in Enum.GetValues<SortMode>()) _sort.Items.Add(m);

        Row("Title", _title);
        Row("", _customColor);
        Row("Orgo color", _color);
        Row("Title bar color", _titleColor);
        Row("Portal folder", portalRow);
        Row("Sort items by", _sort);
        Row("", _rolled);
        Row("", _autoRoll);
        var note = new Label
        {
            AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = SystemColors.GrayText,
            Text = "A portal shows a folder's contents instead of desktop icons. Clearing it turns the Orgo back into a normal one.",
        };
        Row("", note);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
        var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "OK", Width = 90, DialogResult = DialogResult.OK };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(t);
        Controls.Add(buttons);

        _title.Text = fence.Title;
        _customColor.Checked = fence.Color != null || fence.TitleColor != null;
        _color.Value = fence.Color ?? settings.FenceColor;
        _titleColor.Value = fence.TitleColor ?? settings.TitleBarColor;
        _color.Enabled = _titleColor.Enabled = _customColor.Checked;
        _customColor.CheckedChanged += (_, _) => _color.Enabled = _titleColor.Enabled = _customColor.Checked;
        _portal.Text = fence.PortalPath ?? "";
        _sort.SelectedItem = fence.Sort;
        _rolled.Checked = fence.RolledUp;
        _autoRoll.Checked = fence.AutoRollUp;
    }

    /// <summary>Writes the dialog values into the fence. Returns true if the portal folder changed.</summary>
    public bool Apply(Fence fence)
    {
        fence.Title = string.IsNullOrWhiteSpace(_title.Text) ? fence.Title : _title.Text.Trim();
        fence.Color = _customColor.Checked ? _color.Value : null;
        fence.TitleColor = _customColor.Checked ? _titleColor.Value : null;
        var portal = string.IsNullOrWhiteSpace(_portal.Text) ? null : _portal.Text.Trim();
        if (portal != null && !Directory.Exists(portal)) portal = null;
        bool changed = !string.Equals(portal, _originalPortal, StringComparison.OrdinalIgnoreCase);
        fence.PortalPath = portal;
        if (changed && portal != null) fence.Items.Clear();
        fence.Sort = _sort.SelectedItem is SortMode m ? m : fence.Sort;
        if (fence.IsPortal && fence.Sort == SortMode.Manual) fence.Sort = SortMode.Name;
        fence.AutoRollUp = _autoRoll.Checked;
        fence.RolledUp = _rolled.Checked || _autoRoll.Checked;
        fence.Peeking = false;
        return changed;
    }
}
