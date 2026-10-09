namespace DotPixelPainter.Core;

/// <summary>画像1枚から、大きさや向きを変えた新しい画像を作る。元の画像は変えない。</summary>
public static class ImageTransform
{
    /// <summary>
    /// キャンバスの大きさを変える。元の絵を (offsetX, offsetY) の位置に置き、はみ出した部分は切り捨て、空いた部分は透明にする。
    /// </summary>
    public static PixelImage ResizeCanvas(PixelImage source, int width, int height, int offsetX, int offsetY)
    {
        var result = new PixelImage(width, height);
        ReadOnlySpan<uint> src = source.Pixels;
        for (int y = 0; y < source.Height; y++)
        {
            int ty = y + offsetY;
            if ((uint)ty >= (uint)height)
            {
                continue;
            }

            for (int x = 0; x < source.Width; x++)
            {
                result.SetPixel(x + offsetX, ty, src[y * source.Width + x]);
            }
        }

        return result;
    }

    /// <summary>
    /// 絵を拡大・縮小する。色を混ぜない「最近傍」で、ドットがにじまない。
    /// 整数倍の拡大なら 1 ドットがそのまま n×n の四角になる。
    /// </summary>
    public static PixelImage ScaleNearest(PixelImage source, int width, int height)
    {
        var result = new PixelImage(width, height);
        ReadOnlySpan<uint> src = source.Pixels;
        for (int y = 0; y < height; y++)
        {
            // 出力の画素の中心が、元の画像のどの画素に入るか
            int sy = (int)((y * 2L + 1) * source.Height / (height * 2L));
            for (int x = 0; x < width; x++)
            {
                int sx = (int)((x * 2L + 1) * source.Width / (width * 2L));
                result.SetPixel(x, y, src[sy * source.Width + sx]);
            }
        }

        return result;
    }

    public static PixelImage Flip(PixelImage source, bool horizontal)
    {
        int w = source.Width;
        int h = source.Height;
        var result = new PixelImage(w, h);
        ReadOnlySpan<uint> src = source.Pixels;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int sx = horizontal ? w - 1 - x : x;
                int sy = horizontal ? y : h - 1 - y;
                result.SetPixel(x, y, src[sy * w + sx]);
            }
        }

        return result;
    }

    /// <summary>90°回す。幅と高さが入れ替わる。</summary>
    public static PixelImage Rotate90(PixelImage source, bool clockwise)
    {
        int w = source.Width;
        int h = source.Height;
        var result = new PixelImage(h, w);
        ReadOnlySpan<uint> src = source.Pixels;
        for (int y = 0; y < w; y++)
        {
            for (int x = 0; x < h; x++)
            {
                // 時計回り: 結果の (x, y) は元の (y, h-1-x)。反時計回り: 元の (w-1-y, x)
                (int sx, int sy) = clockwise ? (y, h - 1 - x) : (w - 1 - y, x);
                result.SetPixel(x, y, src[sy * w + sx]);
            }
        }

        return result;
    }
}
