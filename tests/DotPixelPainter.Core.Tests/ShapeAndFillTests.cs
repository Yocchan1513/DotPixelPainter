using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class ShapeAndFillTests
{
    private static HashSet<(int X, int Y)> Pixels(IEnumerable<PixelSpan> spans)
    {
        var set = new HashSet<(int, int)>();
        foreach (PixelSpan s in spans)
        {
            for (int x = s.X0; x <= s.X1; x++)
            {
                Assert.True(set.Add((x, s.Y)), $"({x},{s.Y}) が重複しています");
            }
        }

        return set;
    }

    [Fact]
    public void Rectangle_Outline_HasOnlyBorder()
    {
        var pixels = Pixels(ShapeRaster.Rectangle(3, 2, 0, 0, filled: false)); // 逆向きに指定しても同じ

        Assert.Equal(4 + 4 + 1 + 1, pixels.Count); // 4x3 の枠
        Assert.DoesNotContain((1, 1), pixels);
        Assert.Contains((3, 2), pixels);
    }

    [Fact]
    public void Rectangle_Filled_CoversEverything()
    {
        Assert.Equal(12, Pixels(ShapeRaster.Rectangle(0, 0, 3, 2, filled: true)).Count);
    }

    [Theory]
    [InlineData(0, 0, 9, 9)]
    [InlineData(0, 0, 15, 6)]
    [InlineData(2, 3, 3, 12)]
    [InlineData(0, 0, 0, 0)]
    public void Ellipse_StaysInsideBox_AndTouchesAllSides(int x0, int y0, int x1, int y1)
    {
        var pixels = Pixels(ShapeRaster.Ellipse(x0, y0, x1, y1, filled: false));

        Assert.All(pixels, p =>
        {
            Assert.InRange(p.X, x0, x1);
            Assert.InRange(p.Y, y0, y1);
        });
        Assert.Contains(pixels, p => p.X == x0);
        Assert.Contains(pixels, p => p.X == x1);
        Assert.Contains(pixels, p => p.Y == y0);
        Assert.Contains(pixels, p => p.Y == y1);
    }

    [Fact]
    public void Ellipse_Outline_IsSymmetric()
    {
        var pixels = Pixels(ShapeRaster.Ellipse(0, 0, 10, 6, filled: false));

        Assert.All(pixels, p => Assert.Contains((10 - p.X, p.Y), pixels));
        Assert.All(pixels, p => Assert.Contains((p.X, 6 - p.Y), pixels));
    }

    [Fact]
    public void Ellipse_Filled_ContainsCenterAndOutline()
    {
        var outline = Pixels(ShapeRaster.Ellipse(0, 0, 10, 10, filled: false));
        var filled = Pixels(ShapeRaster.Ellipse(0, 0, 10, 10, filled: true));

        Assert.Contains((5, 5), filled);
        Assert.Superset(outline, filled);
        Assert.DoesNotContain((0, 0), filled);
    }

    [Theory]
    [InlineData(10, 1, false, 10, 0)] // ほぼ水平 → 水平
    [InlineData(1, 10, false, 0, 10)] // ほぼ垂直 → 垂直
    [InlineData(7, 5, false, 7, 7)]   // 斜め → 45度
    [InlineData(-3, 8, true, -8, 8)]  // 四角 → 正方形（向きは保つ）
    public void Constrain_SnapsDirection(int x1, int y1, bool square, int ex, int ey)
    {
        Assert.Equal((ex, ey), ShapeRaster.Constrain(0, 0, x1, y1, isLine: !square));
    }

    [Fact]
    public void FloodFill_StopsAtBorder_AndDoesNotLeakDiagonally()
    {
        var doc = new PixelDocument("t", 5, 5);
        var image = doc.ActiveLayer.Image;
        // 斜めにだけ隙間のある枠
        foreach (PixelSpan s in ShapeRaster.Rectangle(0, 0, 4, 4, filled: false))
        {
            for (int x = s.X0; x <= s.X1; x++)
            {
                image.SetPixel(x, s.Y, 0xFF000000);
            }
        }

        PixelStroke stroke = doc.BeginStroke();
        int count = stroke.FloodFill(2, 2, 0xFFFF0000);
        stroke.Commit();

        Assert.Equal(9, count);
        Assert.Equal(0xFFFF0000u, image.GetPixel(1, 1));
        Assert.Equal(0xFF000000u, image.GetPixel(0, 0));
    }

    [Fact]
    public void FloodFill_IsOneUndoStep()
    {
        var doc = new PixelDocument("t", 8, 8);
        PixelStroke stroke = doc.BeginStroke();
        stroke.FloodFill(0, 0, 0xFF00FF00);
        stroke.Commit();

        Assert.Equal(1, doc.History.UndoCount);
        doc.History.Undo();
        Assert.All(doc.ActiveLayer.Image.Pixels.ToArray(), p => Assert.Equal(0u, p));
    }

    [Fact]
    public void FloodFill_SameColor_DoesNothing()
    {
        var doc = new PixelDocument("t", 4, 4);
        PixelStroke stroke = doc.BeginStroke();

        Assert.Equal(0, stroke.FloodFill(1, 1, 0u));
    }

    [Fact]
    public void FloodFill_LargeImage_DoesNotOverflow()
    {
        var doc = new PixelDocument("t", 1024, 1024);
        PixelStroke stroke = doc.BeginStroke();

        Assert.Equal(1024 * 1024, stroke.FloodFill(500, 500, 0xFF123456));
    }

    [Fact]
    public void PlotSpans_ClipsToImage()
    {
        var doc = new PixelDocument("t", 4, 4);
        PixelStroke stroke = doc.BeginStroke();

        Assert.True(stroke.PlotSpans([new PixelSpan(1, -5, 10)], 0xFFFFFFFF));
        Assert.False(stroke.PlotSpans([new PixelSpan(9, 0, 3)], 0xFFFFFFFF));
        Assert.Equal(0xFFFFFFFFu, doc.ActiveLayer.Image.GetPixel(3, 1));
    }
}
