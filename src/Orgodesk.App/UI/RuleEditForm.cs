using System.Drawing;
using System.Windows.Forms;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Rules;

namespace Orgodesk.App.UI;

/// <summary>Edits one auto-sort rule.</summary>
public sealed class RuleEditForm : Form
{
    private readonly TextBox _name = new() { Width = 300 };
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly TextBox _pattern = new() { Width = 300 };
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox _target = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly CheckBox _enabled = new() { Text = "Enabled", AutoSize = true };
    private readonly Label _help = new() { AutoSize = true, MaximumSize = new Size(320, 0), ForeColor = SystemColors.GrayText };
    private readonly Rule _original;

    public Rule Result { get; private set; }

    public RuleEditForm(Rule rule, Layout layout)
    {
        _original = rule;
        Result = rule.Clone();
        Text = "Auto-sort rule";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(500, 330);

        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, Control c)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, 0, r);
            c.Margin = new Padding(3, 4, 3, 4);
            t.Controls.Add(c, 1, r);
        }
        foreach (RuleKind k in Enum.GetValues<RuleKind>()) _kind.Items.Add(k);
        foreach (ItemType it in Enum.GetValues<ItemType>()) _type.Items.Add(it);
        foreach (var page in layout.Pages)
            foreach (var f in page.Fences.Where(f => !f.IsPortal))
                _target.Items.Add(new FenceChoice(f.Id, layout.Pages.Count > 1 ? $"{f.Title}  ({page.Name})" : f.Title));

        Row("Name", _name);
        Row("Match by", _kind);
        Row("Pattern", _pattern);
        Row("Item type", _type);
        Row("Move into Orgo", _target);
        Row("", _enabled);
        Row("", _help);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
        var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "OK", Width = 90 };
        ok.Click += (_, _) => Accept();
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(t);
        Controls.Add(buttons);

        _kind.SelectedIndexChanged += (_, _) => UpdateHelp();
        _name.Text = rule.Name;
        _kind.SelectedItem = rule.Kind;
        _pattern.Text = rule.Pattern;
        _type.SelectedItem = rule.ItemType;
        _enabled.Checked = rule.Enabled;
        for (int i = 0; i < _target.Items.Count; i++)
            if (_target.Items[i] is FenceChoice fc && fc.Id == rule.TargetFenceId) _target.SelectedIndex = i;
        if (_target.SelectedIndex < 0 && _target.Items.Count > 0) _target.SelectedIndex = 0;
        UpdateHelp();
    }

    private void UpdateHelp()
    {
        _help.Text = (RuleKind?)_kind.SelectedItem switch
        {
            RuleKind.Extension => "Pattern is a list of extensions, separated by ; (for example: jpg;png;gif).",
            RuleKind.NamePattern => "Pattern is a wildcard on the display name: * matches anything, ? one character (for example: Report*).",
            RuleKind.ItemType => "Matches every item of the chosen type (folders, shortcuts, files, or system items like Recycle Bin).",
            _ => "",
        };
        _pattern.Enabled = (RuleKind?)_kind.SelectedItem != RuleKind.ItemType;
    }

    private void Accept()
    {
        if (_target.SelectedItem is not FenceChoice fc)
        {
            MessageBox.Show(this, "Create an Orgo first, then point the rule at it.", "Rule", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var kind = _kind.SelectedItem is RuleKind k ? k : RuleKind.Extension;
        if (kind != RuleKind.ItemType && string.IsNullOrWhiteSpace(_pattern.Text))
        {
            MessageBox.Show(this, "Enter a pattern.", "Rule", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var type = _type.SelectedItem is ItemType it ? it : ItemType.Any;
        if (kind == RuleKind.ItemType && type == ItemType.Any)
        {
            MessageBox.Show(this, "Choose an item type other than Any.", "Rule", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Result = new Rule
        {
            Id = _original.Id,
            Name = string.IsNullOrWhiteSpace(_name.Text) ? (kind == RuleKind.ItemType ? type.ToString() : _pattern.Text) : _name.Text.Trim(),
            Kind = kind,
            Pattern = _pattern.Text.Trim(),
            ItemType = type,
            TargetFenceId = fc.Id,
            Enabled = _enabled.Checked,
        };
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record FenceChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
