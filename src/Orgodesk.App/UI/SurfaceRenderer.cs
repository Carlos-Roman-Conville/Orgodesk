using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Orgodesk.Core.Fences;
using Orgodesk.Windows.Desktop;
using Orgodesk.Windows.Shell;

namespace Orgodesk.App.UI;

/// <summary>Everything transient the renderer needs besides the model.</summary>
public sealed class FrameState
{
    public IReadOnlySet<ItemRef> Selection { get; init; } = new HashSet<ItemRef>();
    public ItemRef? Hover { get; init; }
    public string? HoverFenceId { get; init; }
    public Rectangle? Lasso { get; init; }
    public Rectangle? CreateRect { get; init; }
    public DropTarget? Drop { get; init; }
    public bool QuickHidden { get; init; }
    public string? Hud { get; init; }
    public IReadOnlySet<ItemRef>? Dragging { get; init; }
}

/// <summary>Draws fences, icons and feedback into a premultiplied-alpha bitmap. Layout coordinates are screen pixels.</summary>
public sealed class SurfaceRenderer : IDisposable
{
    private readonly DesktopModel _model;
    private readonly Dictionary<(string family, float size, FontStyle style), Font> _fonts = new();

    public SurfaceRenderer(DesktopModel model) => _model = model;

    private Font Font(string family, float sizePt, FontStyle style)
    {
        var k = (family, sizePt, style);
        if (!_fonts.TryGetValue(k, out var f))
        {
            try { f = new Font(family, sizePt, style, GraphicsUnit.Point); }
            catch { f = new Font(FontFamily.GenericSansSerif, sizePt, style, GraphicsUnit.Point); }
            _fonts[k] = f;
        }
        return f;
    }

    /// <summary>
    /// GDI+ has no font fallback, so emoji (characters outside the basic plane) draw as empty boxes in most fonts.
    /// Labels that contain them are drawn in Segoe UI Emoji, which also covers Latin letters and accents (FC-30).
    /// </summary>
    private Font LabelFontFor(string text, string family, float sizePt, FontStyle style)
    {
        foreach (var c in text)
            if (char.IsSurrogate(c)) return Font("Segoe UI Emoji", sizePt, style);
        return Font(family, sizePt, style);
    }

    public void ClearFonts()
    {
        foreach (var f in _fonts.Values) f.Dispose();
        _fonts.Clear();
    }

    public LayoutMetrics MetricsAt(Point screenPoint) => _model.BaseMetrics.Scale(ScreenInfo.ScaleAt(screenPoint));

    private static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    public void Render(Graphics g, Point origin, FrameState frame)
    {
        g.Clear(Color.FromArgb(1, 0, 0, 0)); // almost invisible, but keeps every pixel hit-testable
        if (frame.QuickHidden)
        {
            DrawHud(g, origin, frame.Hud);
            return;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TranslateTransform(-origin.X, -origin.Y);

        var s = _model.Settings;
        var page = _model.Layout.ActivePage;

        // Free icons
        foreach (var (key, pos) in page.FreeItems)
        {
            if (!_model.DesktopItems.TryGetValue(key, out var item)) continue;
            var r = new ItemRef(null, key);
            if (frame.Dragging != null && frame.Dragging.Contains(r)) continue;
            var m = MetricsAt(pos);
            DrawItemCell(g, item, FreeGrid.CellAt(pos, m), m, frame.Selection.Contains(r), frame.Hover == r);
        }

        // Fences, in order (last on top)
        foreach (var fence in page.Fences)
            DrawFence(g, fence, frame);

        // Drop feedback
        if (frame.Drop is { Valid: true } drop)
        {
            if (drop.Fence != null && !drop.Fence.IsPortal && !drop.Fence.IsCollapsed)
            {
                var m = MetricsAt(Center(drop.Fence.Bounds));
                var content = FenceLayoutEngine.ContentArea(drop.Fence, m);
                var keys = _model.ItemsOf(drop.Fence).Select(i => i.Key).ToList();
                var cells = FenceLayoutEngine.Arrange(keys, content, m, drop.Fence.ScrollOffset);
                Rectangle marker;
                if (keys.Count == 0) marker = new Rectangle(content.X, content.Y, 3, m.CellHeight);
                else if (drop.InsertIndex < cells.Count) marker = new Rectangle(cells[drop.InsertIndex].Bounds.X - 1, cells[drop.InsertIndex].Bounds.Y, 3, m.CellHeight);
                else marker = new Rectangle(cells[^1].Bounds.Right - 2, cells[^1].Bounds.Y, 3, m.CellHeight);
                using var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 3);
                g.SetClip(content);
                g.DrawLine(pen, marker.X + 1, marker.Y + 2, marker.X + 1, marker.Bottom - 2);
                g.ResetClip();
            }
            else if (drop.Fence != null)
            {
                using var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 2);
                var vb = FenceLayoutEngine.VisibleBounds(drop.Fence, MetricsAt(Center(drop.Fence.Bounds)));
                g.DrawRectangle(pen, vb.X + 1, vb.Y + 1, vb.Width - 2, vb.Height - 2);
            }
            else if (drop.FreePosition is { } fp)
            {
                var m = MetricsAt(fp);
                using var pen = new Pen(Color.FromArgb(160, 255, 255, 255), 1) { DashStyle = DashStyle.Dash };
                var cell = FreeGrid.CellAt(fp, m);
                g.DrawRectangle(pen, cell.X, cell.Y, cell.Width - 1, cell.Height - 1);
            }
        }

        if (frame.Lasso is { } lasso && lasso.Width > 0 && lasso.Height > 0)
        {
            using var fill = new SolidBrush(Color.FromArgb(60, 51, 153, 255));
            using var pen = new Pen(Color.FromArgb(200, 51, 153, 255), 1);
            g.FillRectangle(fill, lasso);
            g.DrawRectangle(pen, lasso.X, lasso.Y, lasso.Width - 1, lasso.Height - 1);
        }

        if (frame.CreateRect is { } cr && cr.Width > 0 && cr.Height > 0)
        {
            var body = ColorUtil.Parse(s.FenceColor, Color.FromArgb(140, 30, 30, 30));
            using var fill = new SolidBrush(ColorUtil.WithAlpha(body, body.A / 2));
            using var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1) { DashStyle = DashStyle.Dash };
            g.FillRectangle(fill, cr);
            g.DrawRectangle(pen, cr.X, cr.Y, cr.Width - 1, cr.Height - 1);
        }

        g.ResetTransform();
        if (s.ShowPageIndicator && _model.Layout.Pages.Count > 1) DrawPageDots(g, origin);
        DrawHud(g, origin, frame.Hud);
    }

    private void DrawFence(Graphics g, Fence fence, FrameState frame)
    {
        var s = _model.Settings;
        var m = MetricsAt(Center(fence.Bounds));
        double scale = m.CellWidth / (double)Math.Max(1, s.CellWidth);
        int radius = Math.Max(0, (int)Math.Round(s.CornerRadius * scale));
        var bodyColor = ColorUtil.Parse(fence.Color ?? s.FenceColor, Color.FromArgb(140, 30, 30, 30));
        var titleColor = ColorUtil.Parse(fence.TitleColor ?? s.TitleBarColor, Color.FromArgb(180, 20, 20, 20));
        if (frame.HoverFenceId == fence.Id) titleColor = ColorUtil.Lighter(titleColor, 18);
        var titleBar = FenceLayoutEngine.TitleBar(fence, m);

        if (fence.IsCollapsed)
        {
            using var path = RoundedRect(titleBar, radius);
            using var brush = new SolidBrush(titleColor);
            g.FillPath(brush, path);
        }
        else
        {
            using var body = RoundedRect(fence.Bounds, radius);
            using var brush = new SolidBrush(bodyColor);
            g.FillPath(brush, body);
            using var top = RoundedRectTop(titleBar, radius);
            using var tbrush = new SolidBrush(titleColor);
            g.FillPath(tbrush, top);
        }

        // Title
        var titleFont = Font(s.LabelFont, (float)(s.TitleFontSize * scale), FontStyle.Bold);
        using (var tb = new SolidBrush(ColorUtil.Parse(s.TitleTextColor, Color.White)))
        using (var fmt = new StringFormat(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
        {
            var tr = new Rectangle(titleBar.X + m.Padding + 2, titleBar.Y, titleBar.Width - 2 * m.Padding - 4, titleBar.Height);
            string title = fence.Title;
            if (fence.IsPortal && string.IsNullOrWhiteSpace(title)) title = Path.GetFileName(fence.PortalPath!.TrimEnd('\\'));
            g.DrawString(title, LabelFontFor(title, s.LabelFont, titleFont.SizeInPoints, FontStyle.Bold), tb, tr, fmt);
        }
        if (fence.IsCollapsed) return;

        // Items
        var content = FenceLayoutEngine.ContentArea(fence, m);
        var items = _model.ItemsOf(fence);
        var keys = items.Select(i => i.Key).ToList();
        fence.ScrollOffset = FenceLayoutEngine.ClampScroll(fence.ScrollOffset, keys.Count, content, m);
        var cells = FenceLayoutEngine.Arrange(keys, content, m, fence.ScrollOffset);
        g.SetClip(content);
        for (int i = 0; i < cells.Count; i++)
        {
            if (!cells[i].Visible) continue;
            var r = new ItemRef(fence.IsPortal ? fence.Id : null, items[i].Key);
            if (frame.Dragging != null && frame.Dragging.Contains(r)) continue;
            DrawItemCell(g, items[i], cells[i].Bounds, m, frame.Selection.Contains(r), frame.Hover == r);
        }
        g.ResetClip();

        // Scrollbar
        int max = FenceLayoutEngine.MaxScroll(keys.Count, content, m);
        if (max > 0)
        {
            int track = content.Height;
            int total = FenceLayoutEngine.ContentHeight(keys.Count, content, m);
            int thumbH = Math.Max(16, track * track / Math.Max(1, total));
            int thumbY = content.Y + (int)((track - thumbH) * (fence.ScrollOffset / (double)max));
            using var brush = new SolidBrush(Color.FromArgb(120, 255, 255, 255));
            g.FillRectangle(brush, fence.Bounds.Right - m.Padding + 1, thumbY, Math.Max(2, m.Padding - 3), thumbH);
        }
    }

    public void DrawItemCell(Graphics g, ShellItemInfo item, Rectangle cell, LayoutMetrics m, bool selected, bool hovered)
    {
        var s = _model.Settings;
        if (selected || hovered)
        {
            var sel = ColorUtil.Parse(s.SelectionColor, Color.FromArgb(90, 51, 153, 255));
            if (!selected) sel = ColorUtil.WithAlpha(sel, sel.A / 3);
            using var brush = new SolidBrush(sel);
            using var pen = new Pen(ColorUtil.WithAlpha(sel, Math.Min(255, sel.A + 80)), 1);
            using var path = RoundedRect(new Rectangle(cell.X + 1, cell.Y + 1, cell.Width - 2, cell.Height - 2), 3);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        int iconTop = cell.Y + Math.Max(2, m.Padding / 2);
        var icon = _model.Icons.GetIcon(item, m.IconSize);
        var iconRect = new Rectangle(cell.X + (cell.Width - m.IconSize) / 2, iconTop, m.IconSize, m.IconSize);
        if (icon != null) g.DrawImage(icon, iconRect);
        else
        {
            using var pen = new Pen(Color.FromArgb(120, 255, 255, 255));
            g.DrawRectangle(pen, iconRect);
        }

        double scale = m.CellWidth / (double)Math.Max(1, s.CellWidth);
        var font = LabelFontFor(item.DisplayName, s.LabelFont, (float)(s.LabelFontSize * scale), FontStyle.Regular);
        var labelRect = new Rectangle(cell.X + 2, iconRect.Bottom + 2, cell.Width - 4, Math.Max(0, cell.Bottom - iconRect.Bottom - 4));
        using var fmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.LineLimit,
        };
        var textColor = ColorUtil.Parse(s.LabelColor, Color.White);
        if (s.LabelShadow)
        {
            using var shadow = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            var sr = labelRect; sr.Offset(1, 1);
            g.DrawString(item.DisplayName, font, shadow, sr, fmt);
        }
        using var tbrush = new SolidBrush(textColor);
        g.DrawString(item.DisplayName, font, tbrush, labelRect, fmt);
    }

    /// <summary>The label rectangle of a cell, for the inline rename editor.</summary>
    public Rectangle LabelRect(Rectangle cell, LayoutMetrics m)
    {
        int iconTop = cell.Y + Math.Max(2, m.Padding / 2);
        return new Rectangle(cell.X + 2, iconTop + m.IconSize + 2, cell.Width - 4, Math.Max(0, cell.Bottom - iconTop - m.IconSize - 4));
    }

    private void DrawPageDots(Graphics g, Point origin)
    {
        var area = ScreenInfo.PrimaryWorkArea;
        double scale = ScreenInfo.ScaleAt(Center(area));
        int r = (int)Math.Round(4 * scale), gap = (int)Math.Round(10 * scale);
        int count = _model.Layout.Pages.Count;
        int total = count * (2 * r) + (count - 1) * gap;
        int x = area.X + (area.Width - total) / 2 - origin.X;
        int y = area.Bottom - (int)Math.Round(14 * scale) - origin.Y;
        using var on = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
        using var off = new SolidBrush(Color.FromArgb(90, 255, 255, 255));
        for (int i = 0; i < count; i++)
        {
            g.FillEllipse(i == _model.Layout.ActivePageIndex ? on : off, x, y, 2 * r, 2 * r);
            x += 2 * r + gap;
        }
    }

    private void DrawHud(Graphics g, Point origin, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var area = ScreenInfo.PrimaryWorkArea;
        double scale = ScreenInfo.ScaleAt(Center(area));
        var font = Font(_model.Settings.LabelFont, (float)(16 * scale), FontStyle.Bold);
        var size = g.MeasureString(text, font);
        int pad = (int)Math.Round(16 * scale);
        var box = new Rectangle(
            area.X + (area.Width - (int)size.Width) / 2 - pad - origin.X,
            area.Y + (area.Height - (int)size.Height) / 2 - pad - origin.Y,
            (int)size.Width + 2 * pad, (int)size.Height + 2 * pad);
        using var path = RoundedRect(box, pad / 2);
        using var brush = new SolidBrush(Color.FromArgb(190, 20, 20, 20));
        g.FillPath(brush, path);
        using var tb = new SolidBrush(Color.White);
        g.DrawString(text, font, tb, box.X + pad, box.Y + pad);
    }

    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || r.Width < 2 * radius || r.Height < 2 * radius)
        {
            path.AddRectangle(r);
            return path;
        }
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath RoundedRectTop(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || r.Width < 2 * radius || r.Height < radius)
        {
            path.AddRectangle(r);
            return path;
        }
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        path.CloseFigure();
        return path;
    }

    public void Dispose() => ClearFonts();
}
