using System.Drawing;
using System.Windows.Forms;

namespace Orgodesk.App.UI;

/// <summary>
/// The dialogs are laid out in 96-dpi pixels. WinForms' automatic DPI scaling did not scale them (fonts grew, layout
/// didn't, so text and buttons were clipped at 150%; FC-35). Each dialog turns automatic scaling off and calls
/// <see cref="Apply"/>, which scales the whole dialog by its monitor's DPI when it loads, then re-centres it.
/// </summary>
internal static class UiScale
{
    public static void Apply(Form form)
    {
        form.AutoScaleMode = AutoScaleMode.None;
        form.Load += (_, _) =>
        {
            float k = form.DeviceDpi / 96f;
            if (Math.Abs(k - 1f) < 0.01f) return;
            form.Scale(new SizeF(k, k));
            // Never larger than the screen's usable area (taskbar excluded), so bottom buttons stay reachable.
            var screen = Screen.FromControl(form).WorkingArea;
            if (form.Width > screen.Width || form.Height > screen.Height)
                form.Size = new Size(Math.Min(form.Width, screen.Width), Math.Min(form.Height, screen.Height));
            var anchor = form.Owner != null && form.StartPosition == FormStartPosition.CenterParent
                ? form.Owner.Bounds
                : Screen.FromControl(form).WorkingArea;
            if (form.StartPosition is FormStartPosition.CenterScreen or FormStartPosition.CenterParent)
                form.Location = new Point(anchor.X + (anchor.Width - form.Width) / 2, anchor.Y + (anchor.Height - form.Height) / 2);
        };
    }
}
