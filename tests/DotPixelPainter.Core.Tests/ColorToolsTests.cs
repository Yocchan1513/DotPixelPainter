using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class ColorToolsTests
{
    private const uint Red = 0xFFFF0000, DarkRed = 0xFFC00000, Blue = 0xFF0000FF, White = 0xFFFFFFFF, Black = 0xFF000000;

    [Fact]
    public void CountColors_OrdersByCount_AndSkipsTransparent()
    {
        var image = new PixelImage(3, 1);
        image.SetPixel(0, 0, Blue);
        image.SetPixel(1, 0, Blue);
        image.SetPixel(2, 0, Red);
        var empty = new PixelImage(3, 1); // 透明だけ

        var counts = ColorTools.CountColors([image, empty]);

        Assert.Equal([(Blue, 2), (Red, 1)], counts);
    }

    [Fact]
    public void CountColors_OnlyInsideArea()
    {
        var image = new PixelImage(3, 1);
        image.SetPixel(0, 0, Blue);
        image.SetPixel(2, 0, Red);

        Assert.Equal([(Red, 1)], ColorTools.CountColors([image], new PixelRect(1, 0, 2, 1)));
    }

    [Fact]
    public void Nearest_PicksClosestColor_AndKeepsAlpha()
    {
        Assert.Equal(DarkRed, ColorTools.Nearest(0xFFB01010, [Blue, DarkRed, White]));
        Assert.Equal(0x80C00000u, ColorTools.Nearest(0x80B01010, [Blue, DarkRed, White]));
    }

    [Fact]
    public void MedianCut_SeparatesClearlyDifferentGroups()
    {
        var colors = new List<(uint, int)> { (0xFFFF0000, 5), (0xFFF00000, 5), (0xFF0000FF, 5), (0xFF0000F0, 5) };

        List<uint> two = ColorTools.MedianCut(colors, 2);

        Assert.Equal(2, two.Count);
        Assert.Contains(two, c => (c >> 16 & 0xFF) > 200 && (c & 0xFF) < 50);  // 赤のまとまり
        Assert.Contains(two, c => (c & 0xFF) > 200 && (c >> 16 & 0xFF) < 50);  // 青のまとまり
    }

    [Fact]
    public void MedianCut_FewerColorsThanAsked_ReturnsThemAsIs()
    {
        Assert.Equal([Red, Blue], ColorTools.MedianCut([(Red, 3), (Blue, 1)], 16));
    }

    [Fact]
    public void ReplaceColor_AllLayers_IsOneUndoStep()
    {
        var doc = new PixelDocument("t", 2, 1);
        doc.ActiveLayer.Image.SetPixel(0, 0, Red);
        Layer upper = doc.AddLayer();
        upper.Image.SetPixel(1, 0, Red);

        Assert.Equal(2, doc.ReplaceColor(Red, Blue, allLayers: true));
        Assert.Equal(Blue, doc.Layers[0].Image.GetPixel(0, 0));
        Assert.Equal(Blue, upper.Image.GetPixel(1, 0));

        doc.History.Undo();
        Assert.Equal(Red, doc.Layers[0].Image.GetPixel(0, 0));
        Assert.Equal(Red, upper.Image.GetPixel(1, 0));
    }

    [Fact]
    public void ReplaceColor_ActiveLayerAndArea()
    {
        var doc = new PixelDocument("t", 3, 1);
        Layer lower = doc.ActiveLayer;
        lower.Image.SetPixel(0, 0, Red);
        lower.Image.SetPixel(2, 0, Red);
        Layer upper = doc.AddLayer();
        upper.Image.SetPixel(2, 0, Red);

        doc.SelectLayer(0);
        Assert.Equal(1, doc.ReplaceColor(Red, Blue, allLayers: false, new PixelRect(1, 0, 2, 1)));
        Assert.Equal(Red, lower.Image.GetPixel(0, 0));   // 範囲の外
        Assert.Equal(Blue, lower.Image.GetPixel(2, 0));
        Assert.Equal(Red, upper.Image.GetPixel(2, 0));   // ほかのレイヤー
    }

    [Fact]
    public void ReduceColors_MapsToPalette_LeavesTransparent()
    {
        var doc = new PixelDocument("t", 3, 1);
        doc.ActiveLayer.Image.SetPixel(0, 0, 0xFF101010);
        doc.ActiveLayer.Image.SetPixel(1, 0, 0xFFF0F0F0);

        doc.ReduceColors([Black, White], allLayers: false);

        Assert.Equal(Black, doc.ActiveLayer.Image.GetPixel(0, 0));
        Assert.Equal(White, doc.ActiveLayer.Image.GetPixel(1, 0));
        Assert.Equal(0u, doc.ActiveLayer.Image.GetPixel(2, 0));
    }
}
