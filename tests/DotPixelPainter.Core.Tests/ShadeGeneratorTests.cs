using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class ShadeGeneratorTests
{
    private const uint Orange = 0xFFE07030;

    [Fact]
    public void Ramp_ContainsBaseColorAtBaseIndex()
    {
        uint[] ramp = ShadeGenerator.Ramp(Orange, count: 8, baseIndex: 3);

        uint b = ramp[3];
        Assert.InRange((int)((b >> 16) & 0xFF), 0xE0 - 2, 0xE0 + 2);
        Assert.InRange((int)((b >> 8) & 0xFF), 0x70 - 2, 0x70 + 2);
        Assert.InRange((int)(b & 0xFF), 0x30 - 2, 0x30 + 2);
    }

    [Fact]
    public void Ramp_GetsBrighterFromLeftToRight()
    {
        uint[] ramp = ShadeGenerator.Ramp(Orange);
        int[] values = [.. ramp.Select(c => ColorHsv.FromArgb(c).V)];

        for (int i = 1; i < values.Length; i++)
        {
            Assert.True(values[i] >= values[i - 1], $"{i - 1}→{i} で暗くなっています");
        }

        Assert.Equal(255, values[^1]);
    }

    [Fact]
    public void Ramp_HueShift_MovesShadowsTowardBlue_AndHighlightsTowardYellow()
    {
        uint[] ramp = ShadeGenerator.Ramp(Orange);
        int baseHue = ColorHsv.FromArgb(Orange).H; // 約 20°

        int darkest = ColorHsv.FromArgb(ramp[0]).H;
        int lightest = ColorHsv.FromArgb(ramp[^2]).H;

        Assert.True(Distance(darkest, ShadeGenerator.ShadowHue) < Distance(baseHue, ShadeGenerator.ShadowHue));
        Assert.True(Distance(lightest, ShadeGenerator.HighlightHue) < Distance(baseHue, ShadeGenerator.HighlightHue));
    }

    [Fact]
    public void Ramp_WithoutHueShift_KeepsHue()
    {
        uint[] ramp = ShadeGenerator.Ramp(Orange, hueShift: false);
        int baseHue = ColorHsv.FromArgb(Orange).H;

        Assert.All(ramp.Take(6), c => Assert.InRange(Distance(ColorHsv.FromArgb(c).H, baseHue), 0, 3));
    }

    [Fact]
    public void Ramp_Gray_StaysGray()
    {
        uint[] ramp = ShadeGenerator.Ramp(0xFF808080);

        Assert.All(ramp, c => Assert.Equal(0, ColorHsv.FromArgb(c).S));
    }

    [Fact]
    public void Grid_RunsFromGrayToVivid_AndBrightToDark()
    {
        uint[,] grid = ShadeGenerator.Grid(hue: 120, columns: 8, rows: 6);

        Assert.Equal(0xFFFFFFFFu, grid[0, 0]);                  // 左上は白（鮮やかさ0・明るさ最大）
        Assert.Equal(0xFF00FF00u, grid[0, 7]);                  // 右上は純色の緑
        Assert.True(ColorHsv.FromArgb(grid[5, 7]).V < 80);      // 下ほど暗い
    }

    [Theory]
    [InlineData(20, 240, 30, 350)]   // 近い回り方（反時計回り）で青へ
    [InlineData(20, 60, 30, 50)]
    [InlineData(55, 60, 30, 60)]     // 行き過ぎない
    [InlineData(300, 60, 30, 330)]   // 0° をまたぐ
    public void TurnToward_TakesShortestWay(int from, int target, int amount, int expected)
    {
        Assert.Equal(expected, ShadeGenerator.TurnToward(from, target, amount));
    }

    private static int Distance(int a, int b)
    {
        int d = Math.Abs(a - b) % 360;
        return Math.Min(d, 360 - d);
    }
}
