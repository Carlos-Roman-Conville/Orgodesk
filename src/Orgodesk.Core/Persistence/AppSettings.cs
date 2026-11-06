namespace Orgodesk.Core.Persistence;

/// <summary>Global appearance and behavior settings. Sizes are at 96 dpi; the UI scales them per monitor.</summary>
public sealed class AppSettings
{
    // Appearance
    public string FenceColor { get; set; } = "#8C1E1E1E";
    public string TitleBarColor { get; set; } = "#B4141414";
    public string TitleTextColor { get; set; } = "#FFF0F0F0";
    public string LabelColor { get; set; } = "#FFFFFFFF";
    public string SelectionColor { get; set; } = "#5A3399FF";
    public string LabelFont { get; set; } = "Segoe UI";
    public float LabelFontSize { get; set; } = 9f;
    public float TitleFontSize { get; set; } = 10f;
    public int CornerRadius { get; set; } = 6;
    public bool LabelShadow { get; set; } = true;

    // Grid
    public int IconSize { get; set; } = 48;
    public int CellWidth { get; set; } = 88;
    public int CellHeight { get; set; } = 92;
    public int TitleBarHeight { get; set; } = 26;
    public int FencePadding { get; set; } = 6;

    // Behavior
    public bool QuickHideOnDoubleClick { get; set; } = true;
    public bool ToggleOnDoubleRightClick { get; set; } = true;
    public bool SnapFreeIconsToGrid { get; set; } = true;
    public bool SnapFences { get; set; } = true;
    public int SnapDistance { get; set; } = 12;
    public bool EnablePages { get; set; } = false;
    public int PageSwipeThreshold { get; set; } = 120;
    public bool StartWithWindows { get; set; }
    public bool ShowPageIndicator { get; set; } = true;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
