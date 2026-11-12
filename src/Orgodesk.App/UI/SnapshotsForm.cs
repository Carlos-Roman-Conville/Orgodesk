using System.Drawing;
using System.Windows.Forms;
using Orgodesk.Core.Fences;
using Orgodesk.Core.Persistence;

namespace Orgodesk.App.UI;

/// <summary>Save, restore and delete named layout snapshots.</summary>
public sealed class SnapshotsForm : Form
{
    private readonly LayoutStore _store;
    private readonly DesktopModel _model;
    private readonly DesktopSurface _surface;
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, Dock = DockStyle.Fill };

    public SnapshotsForm(LayoutStore store, DesktopModel model, DesktopSurface surface)
    {
        _store = store;
        _model = model;
        _surface = surface;
        Text = "Layout snapshots";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 320);
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        UiScale.Apply(this);   // scale the 96-dpi layout at 125-200% (FC-35)
        Font = new Font("Segoe UI", 9f);

        _list.Columns.Add("Name", 280);
        _list.Columns.Add("Saved", 180);
        _list.DoubleClick += (_, _) => Restore();

        var side = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.TopDown, Width = 130, Padding = new Padding(6) };
        Button B(string text, EventHandler h) { var b = new Button { Text = text, Width = 112 }; b.Click += h; return b; }
        side.Controls.Add(B("Save current...", (_, _) => SaveCurrent()));
        side.Controls.Add(B("Restore", (_, _) => Restore()));
        side.Controls.Add(B("Delete", (_, _) => Delete()));
        side.Controls.Add(new Label { Height = 12 });
        side.Controls.Add(B("Close", (_, _) => Close()));

        Controls.Add(_list);
        Controls.Add(side);
        Load += (_, _) => Reload();
    }

    private void Reload()
    {
        _list.Items.Clear();
        foreach (var s in _store.ListSnapshots())
        {
            var li = new ListViewItem(s.Name) { Tag = s };
            li.SubItems.Add(s.SavedAt.ToString("yyyy-MM-dd HH:mm"));
            _list.Items.Add(li);
        }
    }

    private SnapshotInfo? Selected => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as SnapshotInfo : null;

    private void SaveCurrent()
    {
        var name = Prompt.Show("Save snapshot", "Snapshot name:", DateTime.Now.ToString("yyyy-MM-dd HH-mm"), this);
        if (string.IsNullOrWhiteSpace(name)) return;
        _surface.SaveNow();
        _store.SaveSnapshot(name, _model.Layout);
        Reload();
    }

    private void Restore()
    {
        if (Selected is not { } s) return;
        if (MessageBox.Show(this, $"Replace the current layout with \"{s.Name}\"?", "Restore snapshot",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        var layout = _store.LoadSnapshot(s.Name);
        if (layout == null)
        {
            MessageBox.Show(this, "That snapshot could not be read.", "Restore snapshot", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _surface.ReplaceLayout(layout);
        Close();
    }

    private void Delete()
    {
        if (Selected is not { } s) return;
        if (MessageBox.Show(this, $"Delete snapshot \"{s.Name}\"?", "Delete snapshot",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        _store.DeleteSnapshot(s.Name);
        Reload();
    }
}
