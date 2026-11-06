using System.Drawing;
using System.Windows.Forms;

namespace Orgodesk.App.UI;

/// <summary>A borderless text box floated over the desktop for inline renames.</summary>
public sealed class PopupEditor : Form
{
    private readonly TextBox _box = new();
    private readonly Action<string> _commit;
    private bool _done;

    private PopupEditor(Rectangle screenRect, string text, Font font, bool multiline, Action<string> commit)
    {
        _commit = commit;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        Padding = new Padding(1);
        Bounds = new Rectangle(screenRect.X, screenRect.Y, Math.Max(60, screenRect.Width), Math.Max(20, screenRect.Height));

        _box.BorderStyle = BorderStyle.None;
        _box.Font = font;
        _box.Multiline = multiline;
        _box.TextAlign = multiline ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        _box.Dock = DockStyle.Fill;
        _box.Text = text;
        _box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && (!multiline || !e.Shift)) { e.SuppressKeyPress = true; Finish(true); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Finish(false); }
        };
        Controls.Add(_box);
        Deactivate += (_, _) => Finish(true);
    }

    public static void Show(Rectangle screenRect, string text, Font font, bool multiline, Action<string> commit)
    {
        var editor = new PopupEditor(screenRect, text, font, multiline, commit);
        editor.Show();
        editor.Activate();
        editor._box.Focus();
        // Select the name without the extension, like Explorer.
        int dot = text.LastIndexOf('.');
        editor._box.Select(0, dot > 0 ? dot : text.Length);
    }

    private void Finish(bool commit)
    {
        if (_done) return;
        _done = true;
        var value = _box.Text;
        Close();
        if (commit) _commit(value);
    }
}

/// <summary>A small modal "enter a value" dialog.</summary>
public static class Prompt
{
    public static string? Show(string title, string label, string defaultValue = "", IWin32Window? owner = null)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(360, 110),
            TopMost = owner == null,
        };
        var lbl = new Label { Text = label, Left = 12, Top = 12, Width = 336, AutoSize = false };
        var box = new TextBox { Left = 12, Top = 36, Width = 336, Text = defaultValue };
        var ok = new Button { Text = "OK", Left = 192, Top = 72, Width = 75, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 273, Top = 72, Width = 75, DialogResult = DialogResult.Cancel };
        form.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        box.SelectAll();
        var result = owner != null ? form.ShowDialog(owner) : form.ShowDialog();
        return result == DialogResult.OK ? box.Text : null;
    }
}
