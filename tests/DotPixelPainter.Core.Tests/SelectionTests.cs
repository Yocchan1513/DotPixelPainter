using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class SelectionTests
{
    [Fact]
    public void FromPoints_WorksInAnyDirection()
    {
        Assert.Equal(new PixelRect(2, 3, 4, 2), PixelRect.FromPoints(5, 4, 2, 3));
    }

    [Fact]
    public void Intersect_ClipsOrReturnsNull()
    {
        var image = new PixelRect(0, 0, 8, 8);

        Assert.Equal(new PixelRect(6, 0, 2, 3), new PixelRect(6, -2, 5, 5).Intersect(image));
        Assert.Null(new PixelRect(8, 0, 2, 2).Intersect(image));
    }

    [Fact]
    public void LiftMoveDrop_MovesPixels_AsOneUndoStep()
    {
        var doc = new PixelDocument("t", 8, 8);
        var image = doc.ActiveLayer.Image;
        image.SetPixel(1, 1, 0xFFFF0000);
        image.SetPixel(2, 1, 0xFF00FF00);

        PixelStroke stroke = doc.BeginStroke();
        FloatingSelection floating = FloatingSelection.Lift(stroke, image, new PixelRect(1, 1, 2, 1))!;
        Assert.Equal(0u, image.GetPixel(1, 1)); // 元の場所は透明になる

        floating.MoveBy(3, 2);
        floating.Drop(stroke);
        stroke.Commit();

        Assert.Equal(0xFFFF0000u, image.GetPixel(4, 3));
        Assert.Equal(0xFF00FF00u, image.GetPixel(5, 3));
        Assert.Equal(0u, image.GetPixel(1, 1));
        Assert.Equal(1, doc.History.UndoCount);

        doc.History.Undo();
        Assert.Equal(0xFFFF0000u, image.GetPixel(1, 1));
        Assert.Equal(0u, image.GetPixel(4, 3));
    }

    [Fact]
    public void Drop_TransparentPixelsDoNotEraseBelow()
    {
        var doc = new PixelDocument("t", 4, 4);
        var image = doc.ActiveLayer.Image;
        image.SetPixel(0, 0, 0xFFFF0000);   // 持ち上げる中身（1マスだけ色、隣は透明）
        image.SetPixel(3, 0, 0xFF0000FF);   // 置き先にある絵

        PixelStroke stroke = doc.BeginStroke();
        FloatingSelection floating = FloatingSelection.Lift(stroke, image, new PixelRect(0, 0, 2, 1))!;
        floating.MoveBy(2, 0);              // 透明な2マス目が (3,0) に重なる
        floating.Drop(stroke);
        stroke.Commit();

        Assert.Equal(0xFFFF0000u, image.GetPixel(2, 0));
        Assert.Equal(0xFF0000FFu, image.GetPixel(3, 0)); // 消えていない
    }

    [Fact]
    public void Drop_OutsideImageIsClipped()
    {
        var doc = new PixelDocument("t", 4, 4);
        var image = doc.ActiveLayer.Image;
        image.SetPixel(3, 3, 0xFFFFFFFF);

        PixelStroke stroke = doc.BeginStroke();
        FloatingSelection floating = FloatingSelection.Lift(stroke, image, new PixelRect(3, 3, 1, 1))!;
        floating.MoveBy(5, 5);
        floating.Drop(stroke);
        stroke.Commit();

        Assert.All(image.Pixels.ToArray(), p => Assert.Equal(0u, p)); // はみ出した分は消える
    }

    [Fact]
    public void Lift_OutsideImage_ReturnsNull()
    {
        var doc = new PixelDocument("t", 4, 4);
        Assert.Null(FloatingSelection.Lift(doc.BeginStroke(), doc.ActiveLayer.Image, new PixelRect(10, 10, 2, 2)));
    }

    [Fact]
    public void Composite_ShowsFloatingOnActiveLayer()
    {
        var doc = new PixelDocument("t", 4, 4);
        var image = doc.ActiveLayer.Image;
        image.SetPixel(0, 0, 0xFFFF0000);
        PixelStroke stroke = doc.BeginStroke();
        FloatingSelection floating = FloatingSelection.Lift(stroke, image, new PixelRect(0, 0, 1, 1))!;
        floating.MoveBy(2, 2);

        var shown = doc.Composite(floating);

        Assert.Equal(0xFFFF0000u, shown.GetPixel(2, 2));
        Assert.Equal(0u, shown.GetPixel(0, 0));
        Assert.Equal(0u, image.GetPixel(2, 2)); // レイヤー自体はまだ書き換わっていない
    }
}
