using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class PixelImageTests
{
    [Fact]
    public void SetPixel_OutOfRange_IsIgnored()
    {
        var image = new PixelImage(4, 4);

        Assert.False(image.SetPixel(-1, 0, 0xFFFF0000));
        Assert.False(image.SetPixel(4, 0, 0xFFFF0000));
        Assert.Equal(0u, image.GetPixel(4, 0));
    }

    [Fact]
    public void SetPixel_ReportsOnlyRealChanges()
    {
        var image = new PixelImage(4, 4);

        Assert.True(image.SetPixel(1, 2, 0xFF112233));
        Assert.False(image.SetPixel(1, 2, 0xFF112233));
        Assert.Equal(0xFF112233u, image.GetPixel(1, 2));
    }

    [Fact]
    public void Bgra_RoundTrip_KeepsColors()
    {
        var image = new PixelImage(2, 1);
        image.SetPixel(0, 0, 0x80102030);
        image.SetPixel(1, 0, 0xFFABCDEF);

        var bytes = new byte[8];
        image.CopyToBgra(bytes);
        var copy = new PixelImage(2, 1);
        copy.LoadFromBgra(bytes);

        Assert.Equal(image.Pixels.ToArray(), copy.Pixels.ToArray());
    }

    [Fact]
    public void Premultiplied_ScalesColorByAlpha()
    {
        var image = new PixelImage(1, 1);
        image.SetPixel(0, 0, 0x80FF0000); // 半透明の赤

        var bytes = new byte[4];
        image.CopyToBgraPremultiplied(bytes);

        Assert.Equal([0, 0, 128, 128], bytes);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(PixelImage.MaxSize + 1, 1)]
    public void Constructor_RejectsBadSize(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelImage(width, height));
    }
}
