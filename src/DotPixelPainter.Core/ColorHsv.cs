namespace DotPixelPainter.Core;

/// <summary>
/// HSV 色。H は 0〜359、S と V は 0〜255 の整数（スライダーでそのまま扱える範囲）。
/// </summary>
public readonly record struct ColorHsv(int H, int S, int V)
{
    public static ColorHsv FromArgb(uint argb)
    {
        int r = (int)((argb >> 16) & 0xFF);
        int g = (int)((argb >> 8) & 0xFF);
        int b = (int)(argb & 0xFF);
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        int delta = max - min;

        int s = max == 0 ? 0 : (int)Math.Round(delta * 255.0 / max);
        double h = 0;
        if (delta != 0)
        {
            if (max == r)
            {
                h = 60.0 * (g - b) / delta;
            }
            else if (max == g)
            {
                h = 60.0 * (b - r) / delta + 120;
            }
            else
            {
                h = 60.0 * (r - g) / delta + 240;
            }
        }

        int hue = (int)Math.Round(h);
        hue = ((hue % 360) + 360) % 360;
        return new ColorHsv(hue, s, max);
    }

    /// <summary>RGB に戻す。アルファは別に渡す。</summary>
    public uint ToArgb(byte alpha = 255)
    {
        double s = S / 255.0;
        double v = V / 255.0;
        double c = v * s;
        double hp = (H % 360) / 60.0;
        double x = c * (1 - Math.Abs(hp % 2 - 1));
        (double r1, double g1, double b1) = hp switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        double m = v - c;
        uint r = (uint)Math.Round((r1 + m) * 255);
        uint g = (uint)Math.Round((g1 + m) * 255);
        uint b = (uint)Math.Round((b1 + m) * 255);
        return (uint)alpha << 24 | r << 16 | g << 8 | b;
    }
}
