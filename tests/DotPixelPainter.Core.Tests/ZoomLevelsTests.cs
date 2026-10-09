using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class ZoomLevelsTests
{
    [Theory]
    [InlineData(64, 64, 1200, 900, 12)]   // 64*12=768 ≤ 810
    [InlineData(16, 16, 1200, 900, 48)]   // 16*48=768
    [InlineData(4096, 4096, 1200, 900, 1)] // 収まらなくても 1 倍
    [InlineData(256, 64, 1200, 900, 4)]   // 横長は幅で決まる（256*4=1024 ≤ 1080）
    public void Fit_PicksLargestZoomThatFits(int w, int h, double viewW, double viewH, int expected)
    {
        Assert.Equal(expected, ZoomLevels.Fit(w, h, viewW, viewH));
    }

    [Theory]
    [InlineData(8, 1, 12)]
    [InlineData(8, -1, 6)]
    [InlineData(64, 1, 64)]  // 上限で止まる
    [InlineData(1, -1, 1)]   // 下限で止まる
    [InlineData(10, 1, 12)]  // 一覧にない倍率は近い段階から動かす
    [InlineData(10, -1, 6)]
    public void Next_StepsThroughLevels(int current, int direction, int expected)
    {
        Assert.Equal(expected, ZoomLevels.Next(current, direction));
    }
}
