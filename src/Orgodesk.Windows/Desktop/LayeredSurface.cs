using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Orgodesk.Windows.Shell;

namespace Orgodesk.Windows.Desktop;

/// <summary>
/// A 32-bit top-down DIB section wrapped in a GDI+ Bitmap, selected into a memory DC, so a frame can be
/// drawn with GDI+ and pushed to a WS_EX_LAYERED window with UpdateLayeredWindow without copying.
/// </summary>
public sealed class DibSurface : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        public uint color0;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern bool GdiFlush();

    public int Width { get; }
    public int Height { get; }
    public Bitmap Bitmap { get; }

    private readonly IntPtr _screenDc;
    private readonly IntPtr _memDc;
    private readonly IntPtr _hBitmap;
    private readonly IntPtr _old;
    private bool _disposed;

    public DibSurface(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        var bmi = new BITMAPINFO
        {
            biSize = 40,
            biWidth = Width,
            biHeight = -Height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        _screenDc = User32.GetDC(IntPtr.Zero);
        _memDc = Gdi32.CreateCompatibleDC(_screenDc);
        _hBitmap = CreateDIBSection(_screenDc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        if (_hBitmap == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
        _old = Gdi32.SelectObject(_memDc, _hBitmap);
        Bitmap = new Bitmap(Width, Height, Width * 4, PixelFormat.Format32bppPArgb, bits);
    }

    /// <summary>Presents the current bitmap contents in the layered window. Position is unchanged.</summary>
    public bool Present(IntPtr hwnd)
    {
        if (_disposed || hwnd == IntPtr.Zero) return false;
        GdiFlush();
        var size = new SIZE(Width, Height);
        var src = new POINT(0, 0);
        var blend = new BLENDFUNCTION
        {
            BlendOp = Const.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = Const.AC_SRC_ALPHA,
        };
        return User32.UpdateLayeredWindow(hwnd, _screenDc, IntPtr.Zero, ref size, _memDc, ref src, 0, ref blend, Const.ULW_ALPHA);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Bitmap.Dispose();
        Gdi32.SelectObject(_memDc, _old);
        Gdi32.DeleteObject(_hBitmap);
        Gdi32.DeleteDC(_memDc);
        User32.ReleaseDC(IntPtr.Zero, _screenDc);
    }
}
