using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class DitherPatternTests
{
    private const uint Red = 0xFFFF0000;
    private const uint White = 0xFFFFFFFF;

    private static DitherPattern Checker => DitherPattern.Presets.Single(p => p.Name == "市松 50%");

    [Fact]
    public void Checker_AlternatesAndContinuesAtNegativeCoordinates()
    {
        Assert.True(Checker.On(0, 0));
        Assert.False(Checker.On(1, 0));
        Assert.True(Checker.On(1, 1));
        Assert.False(Checker.On(-1, 0));
        Assert.True(Checker.On(-1, -1));
    }

    [Theory]
    [InlineData("75%", 12)]
    [InlineData("市松 50%", 8)]
    [InlineData("25%", 4)]
    [InlineData("12.5%", 2)]
    [InlineData("6.25%", 1)]
    public void Presets_HaveExpectedDensity(string name, int onIn16)
    {
        DitherPattern pattern = DitherPattern.Presets.Single(p => p.Name == name);
        int on = 0;
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                on += pattern.On(x, y) ? 1 : 0;
            }
        }

        Assert.Equal(onIn16, on);
    }

    [Fact]
    public void Plot_WithPattern_LeavesGapsUntouched()
    {
        var doc = new PixelDocument("t", 2, 1);
        PixelStroke stroke = doc.BeginStroke(pattern: new DrawPattern(Checker));
        stroke.Plot(0, 0, Red);
        stroke.Plot(1, 0, Red);
        stroke.Commit();

        Assert.Equal(Red, doc.ActiveLayer.Image.GetPixel(0, 0));
        Assert.Equal(0u, doc.ActiveLayer.Image.GetPixel(1, 0));
    }

    [Fact]
    public void Plot_WithGapColor_FillsGaps()
    {
        var doc = new PixelDocument("t", 2, 1);
        PixelStroke stroke = doc.BeginStroke(pattern: new DrawPattern(Checker, White));
        stroke.PlotSpans([new PixelSpan(Y: 0, X0: 0, X1: 1)], Red);
        stroke.Commit();

        Assert.Equal(Red, doc.ActiveLayer.Image.GetPixel(0, 0));
        Assert.Equal(White, doc.ActiveLayer.Image.GetPixel(1, 0));
    }

    [Fact]
    public void FloodFill_WithPattern_FillsWholeRegionAsChecker()
    {
        var doc = new PixelDocument("t", 4, 4);
        PixelStroke stroke = doc.BeginStroke(pattern: new DrawPattern(Checker));

        Assert.Equal(16, stroke.FloodFill(0, 0, Red));
        stroke.Commit();

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal((x + y) % 2 == 0 ? Red : 0u, doc.ActiveLayer.Image.GetPixel(x, y));
            }
        }

        Assert.Equal(1, doc.History.UndoCount);
    }

    [Fact]
    public void FloodFill_StopsEvenWhenMaskBlocksEveryPixel()
    {
        // 以前は、塗れない画素が残ると上下の行を何度も調べ直して止まらなかった
        var doc = new PixelDocument("t", 8, 8);
        var mask = new ColorMask { Mode = ColorMaskMode.Protect };
        mask.Add(0);

        PixelStroke stroke = doc.BeginStroke(mask);
        Assert.Equal(64, stroke.FloodFill(3, 3, Red));
        stroke.Commit();

        Assert.False(doc.History.CanUndo);
    }
}
