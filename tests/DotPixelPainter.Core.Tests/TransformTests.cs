using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class TransformTests
{
    // 3x2 の画像:
    //   A B C
    //   D E F
    private const uint A = 0xFF00000A, B = 0xFF00000B, C = 0xFF00000C, D = 0xFF00000D, E = 0xFF00000E, F = 0xFF00000F;

    private static FloatingSelection Sample(int x = 10, int y = 20)
    {
        var image = new PixelImage(3, 2);
        uint[] values = [A, B, C, D, E, F];
        for (int i = 0; i < 6; i++)
        {
            image.SetPixel(i % 3, i / 3, values[i]);
        }

        return FloatingSelection.FromImage(image, x, y);
    }

    [Fact]
    public void FlipHorizontal_ReversesRows()
    {
        FloatingSelection f = Sample();
        f.FlipHorizontal();

        Assert.Equal([C, B, A, F, E, D], f.Pixels.ToArray());
    }

    [Fact]
    public void FlipVertical_ReversesColumns()
    {
        FloatingSelection f = Sample();
        f.FlipVertical();

        Assert.Equal([D, E, F, A, B, C], f.Pixels.ToArray());
    }

    [Fact]
    public void Rotate90_Clockwise()
    {
        FloatingSelection f = Sample();
        f.Rotate90(clockwise: true);

        // D A
        // E B
        // F C
        Assert.Equal(2, f.Bounds.Width);
        Assert.Equal(3, f.Bounds.Height);
        Assert.Equal([D, A, E, B, F, C], f.Pixels.ToArray());
    }

    [Fact]
    public void Rotate90_CounterClockwise()
    {
        FloatingSelection f = Sample();
        f.Rotate90(clockwise: false);

        // C F
        // B E
        // A D
        Assert.Equal([C, F, B, E, A, D], f.Pixels.ToArray());
    }

    [Fact]
    public void Rotate_FourTimes_ReturnsToStart()
    {
        FloatingSelection f = Sample();
        PixelRect before = f.Bounds;
        for (int i = 0; i < 4; i++)
        {
            f.Rotate90(clockwise: true);
        }

        Assert.Equal([A, B, C, D, E, F], f.Pixels.ToArray());
        Assert.Equal(before.Width, f.Bounds.Width);
    }

    [Fact]
    public void Rotate_KeepsCenterRoughly()
    {
        var image = new PixelImage(6, 2);
        FloatingSelection f = FloatingSelection.FromImage(image, 10, 10); // 中心 (13, 11)
        f.Rotate90(clockwise: true);

        Assert.Equal(new PixelRect(12, 8, 2, 6), f.Bounds); // 中心 (13, 11) のまま
    }

    [Fact]
    public void FromImage_ToImage_RoundTrip_AndPasteDropsIntoLayer()
    {
        var doc = new PixelDocument("t", 8, 8);
        FloatingSelection f = Sample(x: 2, y: 3);
        Assert.Equal(B, f.ToImage().GetPixel(1, 0));

        PixelStroke stroke = doc.BeginStroke();
        f.Drop(stroke);
        stroke.Commit();

        Assert.Equal(A, doc.ActiveLayer.Image.GetPixel(2, 3));
        Assert.Equal(F, doc.ActiveLayer.Image.GetPixel(4, 4));
        Assert.Equal(1, doc.History.UndoCount); // 貼り付けも1回で戻せる
    }
}
