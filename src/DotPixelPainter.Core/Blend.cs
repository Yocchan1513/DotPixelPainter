namespace DotPixelPainter.Core;

public static class Blend
{
    /// <summary>src を dst の上に通常合成する（ストレートアルファの source-over）。</summary>
    public static void Normal(PixelImage dst, PixelImage src, double opacity = 1.0)
    {
        if (dst.Width != src.Width || dst.Height != src.Height)
        {
            throw new ArgumentException("合成する画像のサイズが違います。", nameof(src));
        }

        int layerAlpha = (int)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        var s = src.Pixels;
        for (int i = 0; i < s.Length; i++)
        {
            uint sc = s[i];
            int sa = (int)(sc >> 24) * layerAlpha / 255;
            if (sa == 0)
            {
                continue;
            }

            int x = i % dst.Width;
            int y = i / dst.Width;
            if (sa == 255)
            {
                dst.SetPixel(x, y, sc | 0xFF000000);
                continue;
            }

            uint dc = dst.GetPixel(x, y);
            int da = (int)(dc >> 24);
            int outA = sa + da * (255 - sa) / 255;
            uint r = Mix((sc >> 16) & 0xFF, (dc >> 16) & 0xFF, sa, da, outA);
            uint g = Mix((sc >> 8) & 0xFF, (dc >> 8) & 0xFF, sa, da, outA);
            uint b = Mix(sc & 0xFF, dc & 0xFF, sa, da, outA);
            dst.SetPixel(x, y, (uint)outA << 24 | r << 16 | g << 8 | b);
        }
    }

    private static uint Mix(uint sc, uint dc, int sa, int da, int outA)
    {
        // (sc*sa + dc*da*(1-sa)) / outA を 0..255 の整数で計算する
        int num = (int)sc * sa * 255 + (int)dc * da * (255 - sa);
        return (uint)Math.Clamp((num + outA * 255 / 2) / (outA * 255), 0, 255);
    }
}
