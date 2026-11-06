namespace Orgodesk.Core.Fences;

/// <summary>Pixel sizes used to lay out icons. All values are physical pixels for one DPI.</summary>
public sealed record LayoutMetrics(
    int CellWidth,
    int CellHeight,
    int IconSize,
    int TitleBarHeight,
    int Padding)
{
    public static LayoutMetrics Default => new(88, 92, 48, 26, 6);

    /// <summary>Scales the base (96 dpi) metrics for the given DPI factor.</summary>
    public LayoutMetrics Scale(double factor) => new(
        Math.Max(16, (int)Math.Round(CellWidth * factor)),
        Math.Max(16, (int)Math.Round(CellHeight * factor)),
        Math.Max(16, (int)Math.Round(IconSize * factor)),
        Math.Max(12, (int)Math.Round(TitleBarHeight * factor)),
        Math.Max(2, (int)Math.Round(Padding * factor)));
}
