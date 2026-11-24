using System.Drawing;
using System.Drawing.Imaging;
using Orgodesk.Windows.Shell;
using Xunit;

namespace Orgodesk.Commands.Tests;

/// <summary>
/// FC-68: at 150% Windows' 256 px list returns an icon that has no 256 frame as a 72 px picture in the top-left corner
/// of a blank 256 canvas. Orgodesk's check only looked 64 px in, so the whole canvas was shrunk and the icon came out
/// about 20 px wide (Rex's Aseprite, darktable, Tiefsee, Tiled). The shapes below are measured from his desktop.
/// </summary>
public class IconCornerTests
{
    private static Bitmap Canvas(Rectangle opaque)
    {
        var b = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(b);
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(Color.Orange);
        g.FillRectangle(brush, opaque);
        return b;
    }

    [Theory]
    [InlineData(4, 0, 66, 66, 72)]     // Aseprite: a 72 px frame, padded inside
    [InlineData(0, 0, 71, 71, 72)]     // AutoHotkey Dash: a full 72 px frame (the old 64 px check missed both)
    [InlineData(1, 0, 70, 71, 72)]     // darktable
    [InlineData(0, 0, 47, 47, 48)]     // a 48 px picture (100% scaling)
    [InlineData(10, 10, 120, 127, 128)] // a 128 px frame
    public void A_small_picture_in_the_corner_is_cut_to_its_frame(int l, int t, int r, int b, int frame)
    {
        using var bmp = Canvas(Rectangle.FromLTRB(l, t, r + 1, b + 1));
        Assert.Equal(new Rectangle(0, 0, frame, frame), IconProvider.SmallPictureArea(bmp));
    }

    [Theory]
    [InlineData(2, 26, 252, 231)]   // Blender: a real 256 px icon
    [InlineData(0, 0, 255, 255)]    // a full-bleed 256 px icon
    public void A_real_256_icon_is_left_whole(int l, int t, int r, int b)
    {
        using var bmp = Canvas(Rectangle.FromLTRB(l, t, r + 1, b + 1));
        Assert.Null(IconProvider.SmallPictureArea(bmp));
    }

    [Fact]
    public void A_blank_canvas_is_left_alone()
    {
        using var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        Assert.Null(IconProvider.SmallPictureArea(bmp));
    }
}
