using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class ColorAndBrushTests
{
    [Theory]
    [InlineData(0xFFFF0000u, 0, 255, 255)]   // 赤
    [InlineData(0xFF00FF00u, 120, 255, 255)] // 緑
    [InlineData(0xFF0000FFu, 240, 255, 255)] // 青
    [InlineData(0xFFFFFFFFu, 0, 0, 255)]     // 白
    [InlineData(0xFF000000u, 0, 0, 0)]       // 黒
    [InlineData(0xFF808080u, 0, 0, 128)]     // 灰
    public void FromArgb_KnownColors(uint argb, int h, int s, int v)
    {
        Assert.Equal(new ColorHsv(h, s, v), ColorHsv.FromArgb(argb));
    }

    [Fact]
    public void RoundTrip_StaysWithinOneStep()
    {
        var random = new Random(1);
        for (int i = 0; i < 2000; i++)
        {
            uint argb = 0xFF000000 | (uint)random.Next(0, 0x1000000);
            uint back = ColorHsv.FromArgb(argb).ToArgb();
            for (int shift = 0; shift <= 16; shift += 8)
            {
                int a = (int)((argb >> shift) & 0xFF);
                int b = (int)((back >> shift) & 0xFF);
                Assert.True(Math.Abs(a - b) <= 2, $"#{argb:X8} → #{back:X8}");
            }
        }
    }

    [Fact]
    public void ToArgb_KeepsAlpha()
    {
        Assert.Equal(0x80FF0000u, new ColorHsv(0, 255, 255).ToArgb(0x80));
    }

    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(2, true, 4)]   // 小さい丸は四角
    [InlineData(3, false, 9)]
    [InlineData(3, true, 5)]   // 十字
    [InlineData(4, true, 12)]  // 角が欠けた 4x4
    [InlineData(16, false, 256)]
    public void Mask_HasExpectedPixelCount(int size, bool round, int count)
    {
        Assert.Equal(count, BrushMask.Create(size, round).Offsets.Count);
    }

    [Fact]
    public void Mask_RoundIsSymmetric()
    {
        foreach (int size in new[] { 3, 5, 6, 7, 9, 16 })
        {
            var offsets = BrushMask.Create(size, round: true).Offsets.ToHashSet();
            int shift = (size - 1) / 2;
            foreach ((int dx, int dy) in offsets)
            {
                // ずれを 0〜size-1 に戻して、左右・上下に反転しても入っているか
                int x = dx + shift;
                int y = dy + shift;
                Assert.Contains((size - 1 - x - shift, dy), offsets);
                Assert.Contains((dx, size - 1 - y - shift), offsets);
            }
        }
    }

    [Fact]
    public void Stamp_ThickensLine()
    {
        var mask = BrushMask.Create(3, round: false);
        var pixels = mask.Stamp(PixelLine.Enumerate(0, 5, 9, 5)).ToHashSet();

        Assert.Equal(12 * 3, pixels.Count); // 横 -1〜10 の12マス、縦 4〜6 の3マス
        Assert.Contains((-1, 4), pixels);
        Assert.Contains((10, 6), pixels);
    }
}
