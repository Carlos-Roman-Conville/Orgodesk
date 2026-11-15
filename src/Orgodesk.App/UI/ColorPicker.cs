using System.Drawing;
using System.Windows.Forms;

namespace Orgodesk.App.UI;

/// <summary>A color swatch button plus an alpha spinner, editing one "#AARRGGBB" value.</summary>
public sealed class ColorPicker : FlowLayoutPanel
{
    private readonly Button _swatch = new() { Width = 84, Height = 24, Text = "", FlatStyle = FlatStyle.Flat };
    private readonly NumericUpDown _alpha = new() { Minimum = 0, Maximum = 255, Width = 56 };
    private Color _rgb = Color.Gray;

    public event EventHandler? ValueChanged;

    public ColorPicker()
    {
        AutoSize = true;
        FlowDirection = FlowDirection.LeftToRight;
        WrapContents = false;
        Margin = new Padding(0);
        _swatch.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _rgb, FullOpen = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _rgb = dlg.Color;
            Refresh();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };
        _alpha.ValueChanged += (_, _) => { Refresh(); ValueChanged?.Invoke(this, EventArgs.Empty); };
        _swatch.AccessibleName = "Color (press Enter to change)";
        _alpha.AccessibleName = "Transparency, 0 to 255";
        Controls.Add(_swatch);
        Controls.Add(new Label { Text = "alpha", AutoSize = true, Padding = new Padding(6, 5, 2, 0) });
        Controls.Add(_alpha);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]   // runtime-only value (WFO1000, .NET 10)
    public string Value
    {
        get => ColorUtil.ToHex(Color.FromArgb((int)_alpha.Value, _rgb));
        set
        {
            var c = ColorUtil.Parse(value, Color.FromArgb(140, 30, 30, 30));
            _rgb = Color.FromArgb(c.R, c.G, c.B);
            _alpha.Value = c.A;
            Refresh();
        }
    }

    private static double Lum(Color c)
    {
        static double F(int v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * F(c.R) + 0.7152 * F(c.G) + 0.0722 * F(c.B);
    }

    public override void Refresh()
    {
        _swatch.BackColor = _rgb;
        _swatch.Text = $"#{_rgb.R:X2}{_rgb.G:X2}{_rgb.B:X2}";   // the value in words, so the swatch is not colour alone
        _swatch.ForeColor = Lum(_rgb) > 0.179 ? Color.Black : Color.White;   // 0.179 = where black and white give equal WCAG contrast
        base.Refresh();
    }
}
