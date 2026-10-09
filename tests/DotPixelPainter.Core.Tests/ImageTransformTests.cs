using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class ImageTransformTests
{
    private const uint R = 0xFFFF0000, G = 0xFF00FF00, B = 0xFF0000FF, W = 0xFFFFFFFF;

    // 2x2:
    //   R G
    //   B W
    private static PixelImage Sample()
    {
        var image = new PixelImage(2, 2);
        image.SetPixel(0, 0, R);
        image.SetPixel(1, 0, G);
        image.SetPixel(0, 1, B);
        image.SetPixel(1, 1, W);
        return image;
    }

    [Fact]
    public void ScaleNearest_DoublesEachDotIntoSquare()
    {
        PixelImage big = ImageTransform.ScaleNearest(Sample(), 4, 4);

        Assert.Equal([R, R, G, G, R, R, G, G, B, B, W, W, B, B, W, W], big.Pixels.ToArray());
    }

    [Fact]
    public void ScaleNearest_HalvesWithoutMixingColors()
    {
        PixelImage big = ImageTransform.ScaleNearest(Sample(), 4, 4);
        PixelImage back = ImageTransform.ScaleNearest(big, 2, 2);

        Assert.Equal(Sample().Pixels.ToArray(), back.Pixels.ToArray());
    }

    [Fact]
    public void ResizeCanvas_PlacesImageAtOffset_AndCutsOverflow()
    {
        PixelImage larger = ImageTransform.ResizeCanvas(Sample(), 4, 3, 1, 1);
        Assert.Equal(0u, larger.GetPixel(0, 0));
        Assert.Equal(R, larger.GetPixel(1, 1));
        Assert.Equal(W, larger.GetPixel(2, 2));

        PixelImage smaller = ImageTransform.ResizeCanvas(Sample(), 1, 1, -1, -1);
        Assert.Equal(W, smaller.GetPixel(0, 0));
    }

    [Fact]
    public void FlipAndRotate()
    {
        Assert.Equal([G, R, W, B], ImageTransform.Flip(Sample(), horizontal: true).Pixels.ToArray());
        Assert.Equal([B, W, R, G], ImageTransform.Flip(Sample(), horizontal: false).Pixels.ToArray());
        Assert.Equal([B, R, W, G], ImageTransform.Rotate90(Sample(), clockwise: true).Pixels.ToArray());
        Assert.Equal([G, W, R, B], ImageTransform.Rotate90(Sample(), clockwise: false).Pixels.ToArray());
    }

    [Fact]
    public void Rotate_NonSquare_SwapsSize()
    {
        PixelImage tall = ImageTransform.Rotate90(new PixelImage(3, 1), clockwise: true);

        Assert.Equal(1, tall.Width);
        Assert.Equal(3, tall.Height);
    }

    [Fact]
    public void Document_Scale_ChangesAllLayers_AndUndoRestoresSize()
    {
        var doc = new PixelDocument("t", 2, 2);
        doc.ActiveLayer.Image.SetPixel(1, 1, R);
        Layer second = doc.AddLayer();
        second.Image.SetPixel(0, 0, G);

        Assert.True(doc.ScaleImage(6, 4));
        Assert.Equal((6, 4), (doc.Width, doc.Height));
        Assert.All(doc.Layers, l => Assert.Equal((6, 4), (l.Image.Width, l.Image.Height)));
        Assert.Equal(R, doc.Layers[0].Image.GetPixel(5, 3));
        Assert.Equal(G, second.Image.GetPixel(2, 1));

        doc.History.Undo();
        Assert.Equal((2, 2), (doc.Width, doc.Height));
        Assert.Equal(R, doc.Layers[0].Image.GetPixel(1, 1));

        doc.History.Redo();
        Assert.Equal(6, doc.Composite().Width);
    }

    [Fact]
    public void StrokesBeforeAndAfterResize_UndoInOrder()
    {
        var doc = new PixelDocument("t", 2, 2);
        PixelStroke first = doc.BeginStroke();
        first.Plot(1, 1, R);
        first.Commit();

        doc.ResizeCanvas(4, 4, 1, 1);
        PixelStroke second = doc.BeginStroke();
        second.Plot(3, 0, B);
        second.Commit();

        doc.History.Undo(); // 2本目
        Assert.Equal(0u, doc.ActiveLayer.Image.GetPixel(3, 0));
        Assert.Equal(R, doc.ActiveLayer.Image.GetPixel(2, 2));

        doc.History.Undo(); // 大きさの変更
        Assert.Equal(2, doc.Width);
        Assert.Equal(R, doc.ActiveLayer.Image.GetPixel(1, 1));

        doc.History.Undo(); // 1本目
        Assert.Equal(0u, doc.ActiveLayer.Image.GetPixel(1, 1));
    }

    [Fact]
    public void Crop_KeepsOnlySelection()
    {
        var doc = new PixelDocument("t", 4, 4);
        doc.ActiveLayer.Image.SetPixel(2, 1, B);

        doc.Crop(new PixelRect(1, 1, 2, 2));

        Assert.Equal((2, 2), (doc.Width, doc.Height));
        Assert.Equal(B, doc.ActiveLayer.Image.GetPixel(1, 0));
    }

    [Fact]
    public void SameSize_DoesNothing()
    {
        var doc = new PixelDocument("t", 4, 4);

        Assert.False(doc.ScaleImage(4, 4));
        Assert.False(doc.History.CanUndo);
    }
}
