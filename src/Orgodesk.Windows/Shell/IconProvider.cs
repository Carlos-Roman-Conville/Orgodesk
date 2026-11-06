using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Orgodesk.Windows.Shell;

/// <summary>Loads shell icons (with overlays such as the shortcut arrow) at a given pixel size, and caches them.</summary>
public sealed class IconProvider : IDisposable
{
    private readonly Dictionary<(string key, int size), Bitmap> _cache = new();

    public Bitmap? GetIcon(ShellItemInfo item, int size)
    {
        var k = (item.Key, size);
        if (_cache.TryGetValue(k, out var bmp)) return bmp;
        bmp = LoadIcon(item.AbsolutePidl, size);
        if (bmp != null) _cache[k] = bmp;
        return bmp;
    }

    public void Invalidate(string key)
    {
        foreach (var k in _cache.Keys.Where(k => string.Equals(k.key, key, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _cache[k].Dispose();
            _cache.Remove(k);
        }
    }

    public void Clear()
    {
        foreach (var b in _cache.Values) b.Dispose();
        _cache.Clear();
    }

    public void Dispose() => Clear();

    /// <summary>Fetches the icon from the system image lists, with overlays, scaled to <paramref name="size"/>.</summary>
    public static Bitmap? LoadIcon(IntPtr absolutePidl, int size)
    {
        if (absolutePidl == IntPtr.Zero) return null;
        var shfi = new SHFILEINFO();
        var res = Shell32.SHGetFileInfo(absolutePidl, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(),
            Const.SHGFI_PIDL | Const.SHGFI_SYSICONINDEX | Const.SHGFI_OVERLAYINDEX);
        if (res == IntPtr.Zero) return null;
        int iconIndex = shfi.iIcon & 0x00FFFFFF;
        int overlay = (shfi.iIcon >> 24) & 0xFF;
        int flags = Const.ILD_TRANSPARENT | (overlay << 8);

        int listId = size <= 16 ? Const.SHIL_SMALL : size <= 32 ? Const.SHIL_LARGE : size <= 48 ? Const.SHIL_EXTRALARGE : Const.SHIL_JUMBO;
        var bmp = FromImageList(listId, iconIndex, flags);
        if (bmp == null) return null;

        // Jumbo entries for icons without a 256px image come back as a small picture on a big canvas.
        if (listId == Const.SHIL_JUMBO && LooksSmall(bmp, 64))
        {
            bmp.Dispose();
            bmp = FromImageList(Const.SHIL_EXTRALARGE, iconIndex, flags);
            if (bmp == null) return null;
        }

        if (bmp.Width == size && bmp.Height == size) return bmp;
        var scaled = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(bmp, new Rectangle(0, 0, size, size));
        }
        bmp.Dispose();
        return scaled;
    }

    private static Bitmap? FromImageList(int listId, int index, int flags)
    {
        var iid = Iids.IImageList;
        if (Shell32.SHGetImageList(listId, ref iid, out var list) != 0 || list == null) return null;
        if (list.GetIcon(index, flags, out var hIcon) != 0 || hIcon == IntPtr.Zero) return null;
        try
        {
            using var icon = Icon.FromHandle(hIcon);
            return icon.ToBitmap();
        }
        catch
        {
            return null;
        }
        finally
        {
            User32.DestroyIcon(hIcon);
        }
    }

    /// <summary>True if every pixel outside the top-left square of <paramref name="limit"/> px is transparent.</summary>
    private static bool LooksSmall(Bitmap bmp, int limit)
    {
        if (bmp.Width <= limit) return true;
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    byte* row = (byte*)data.Scan0 + y * data.Stride;
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        if (x < limit && y < limit) continue;
                        if (row[x * 4 + 3] != 0) return false;
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return true;
    }
}
