using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class DocumentTests
{
    [Fact]
    public void Composite_UpperOpaqueLayerWins()
    {
        var doc = new PixelDocument("test", 2, 1);
        doc.ActiveLayer.Image.SetPixel(0, 0, 0xFF0000FF);
        doc.AddLayer("上");
        doc.ActiveLayer.Image.SetPixel(0, 0, 0xFFFF0000);

        var result = doc.Composite();

        Assert.Equal(0xFFFF0000u, result.GetPixel(0, 0));
    }

    [Fact]
    public void Composite_SkipsHiddenLayers()
    {
        var doc = new PixelDocument("test", 1, 1);
        doc.ActiveLayer.Image.SetPixel(0, 0, 0xFF0000FF);
        doc.AddLayer("上");
        doc.ActiveLayer.Image.SetPixel(0, 0, 0xFFFF0000);
        doc.SetLayerVisible(doc.ActiveLayerIndex, false);

        Assert.Equal(0xFF0000FFu, doc.Composite().GetPixel(0, 0));
    }

    [Fact]
    public void Composite_HalfOpacityMixesColors()
    {
        var doc = new PixelDocument("test", 1, 1);
        doc.ActiveLayer.Image.SetPixel(0, 0, 0xFF000000);
        doc.AddLayer("上");
        doc.ActiveLayer.Image.SetPixel(0, 0, 0xFFFFFFFF);
        doc.SetLayerOpacity(doc.ActiveLayerIndex, 0.5);

        uint c = doc.Composite().GetPixel(0, 0);

        Assert.Equal(0xFFu, c >> 24);
        Assert.InRange((int)((c >> 16) & 0xFF), 126, 129);
    }

    [Fact]
    public void Composite_TransparentOverTransparentStaysTransparent()
    {
        var doc = new PixelDocument("test", 3, 3);

        Assert.All(doc.Composite().Pixels.ToArray(), p => Assert.Equal(0u, p));
    }

    [Fact]
    public void PixelLine_HasNoGaps()
    {
        var points = PixelLine.Enumerate(0, 0, 7, 3).ToList();

        Assert.Equal((0, 0), points[0]);
        Assert.Equal((7, 3), points[^1]);
        for (int i = 1; i < points.Count; i++)
        {
            Assert.True(Math.Abs(points[i].X - points[i - 1].X) <= 1);
            Assert.True(Math.Abs(points[i].Y - points[i - 1].Y) <= 1);
        }
    }
}
