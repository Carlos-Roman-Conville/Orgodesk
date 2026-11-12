using System.Drawing;
using System.Windows.Forms;

namespace Orgodesk.App.UI;

/// <summary>
/// Owner for every Orgodesk dialog and message box (FC-38). The UI thread is input-attached to Explorer's desktop
/// thread, so a dialog with no owner gets Explorer's desktop window as owner, and Windows disables it while the dialog
/// is open. If Orgodesk then exits or is killed with the dialog open, Explorer's desktop stays disabled and no click on
/// the desktop works until Explorer restarts. This invisible window of Orgodesk's own is the owner instead.
/// </summary>
internal static class DialogOwner
{
    private static Form? _form;

    public static IWin32Window Get()
    {
        if (_form == null || _form.IsDisposed)
        {
            var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
            _form = new Form
            {
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Size = new Size(1, 1),
                Location = new Point(wa.X + wa.Width / 2, wa.Y + wa.Height / 2),
                Opacity = 0,
                TopMost = true,
                Text = "Orgodesk",
            };
            _form.Show();
        }
        return _form;
    }
}
