namespace DotPixelPainter.Core;

/// <summary>キャンバスの表示倍率。ドットがにじまないよう整数倍だけを使う。</summary>
public static class ZoomLevels
{
    public static readonly int[] All = [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64];

    /// <summary>今の倍率から1段階上げる（direction = 1）／下げる（-1）。</summary>
    public static int Next(int current, int direction)
    {
        int i = Array.IndexOf(All, current);
        if (i < 0)
        {
            // 一覧にない倍率なら、いちばん近い段階から動かす
            i = 0;
            while (i < All.Length - 1 && All[i + 1] <= current)
            {
                i++;
            }
        }

        return All[Math.Clamp(i + direction, 0, All.Length - 1)];
    }

    /// <summary>
    /// 画像が表示領域（物理ピクセル）に余白付きで収まる、いちばん大きい倍率。
    /// 収まらないほど大きい画像でも 1 倍を返す。
    /// </summary>
    public static int Fit(int imageWidth, int imageHeight, double viewWidth, double viewHeight, double margin = 0.9)
    {
        double availableW = viewWidth * margin;
        double availableH = viewHeight * margin;
        int best = All[0];
        foreach (int zoom in All)
        {
            if (imageWidth * zoom <= availableW && imageHeight * zoom <= availableH)
            {
                best = zoom;
            }
        }

        return best;
    }
}
