using System.Drawing;
using System.Globalization;

namespace Orgodesk.App.UI;

public static class ColorUtil
{
    /// <summary>Parses "#AARRGGBB" or "#RRGGBB". Falls back to the given color on bad input.</summary>
    public static Color Parse(string? text, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        var s = text.Trim().TrimStart('#');
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8) return fallback;
        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? Color.FromArgb(unchecked((int)v))
            : fallback;
    }

    public static string ToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color WithAlpha(Color c, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), c);

    public static Color Lighter(Color c, int amount) =>
        Color.FromArgb(c.A, Math.Min(255, c.R + amount), Math.Min(255, c.G + amount), Math.Min(255, c.B + amount));
}
