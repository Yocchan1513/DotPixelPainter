namespace DotPixelPainter.Core;

/// <summary>
/// 基準の色から、影やハイライトに使う色の並びを作る（AzPainter2 の「拡張パレット」にあたる機能）。
/// </summary>
public static class ShadeGenerator
{
    /// <summary>影色が寄っていく色相（青）。</summary>
    public const int ShadowHue = 240;

    /// <summary>ハイライトが寄っていく色相（黄）。</summary>
    public const int HighlightHue = 60;

    /// <summary>
    /// 暗い→明るいの並び（count 色）。基準の色は baseIndex 番目に入る。
    /// hueShift が true なら、暗い方は青寄り・明るい方は黄寄りに色相を少しずらす（ドット絵でよく使う付け方）。
    /// </summary>
    public static uint[] Ramp(uint baseArgb, int count = 8, int baseIndex = 3, bool hueShift = true)
    {
        ColorHsv b = ColorHsv.FromArgb(baseArgb);
        var colors = new uint[count];
        int lighterSteps = Math.Max(1, count - 1 - baseIndex);
        for (int i = 0; i < count; i++)
        {
            int step = i - baseIndex; // 負なら暗い方、正なら明るい方
            int h = b.H;
            int s = b.S;
            int v = b.V;
            if (step < 0)
            {
                double t = -step / (double)Math.Max(1, baseIndex); // 0〜1
                v = (int)Math.Round(b.V * (1 - 0.7 * t));
                // 影は少しだけ鮮やかにする（灰色など色のない色はそのまま）
                s = b.S == 0 ? 0 : (int)Math.Round(Math.Min(255, b.S + (255 - b.S) * 0.25 * t));
                if (hueShift && b.S > 0)
                {
                    h = TurnToward(b.H, ShadowHue, (int)Math.Round(24 * t));
                }
            }
            else if (step > 0)
            {
                double t = step / (double)lighterSteps; // 0〜1
                v = (int)Math.Round(b.V + (255 - b.V) * t);
                s = (int)Math.Round(b.S * (1 - 0.65 * t));
                if (hueShift && b.S > 0)
                {
                    h = TurnToward(b.H, HighlightHue, (int)Math.Round(20 * t));
                }
            }

            colors[i] = new ColorHsv(h, Math.Clamp(s, 0, 255), Math.Clamp(v, 0, 255)).ToArgb();
        }

        return colors;
    }

    /// <summary>
    /// 色相を hue に固定して、鮮やかさ（横: 低→高）× 明るさ（縦: 明→暗）を並べた表。[行, 列]。
    /// </summary>
    public static uint[,] Grid(int hue, int columns = 8, int rows = 6)
    {
        var grid = new uint[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            int v = (int)Math.Round(255 * (1 - 0.8 * r / Math.Max(1, rows - 1)));
            for (int c = 0; c < columns; c++)
            {
                int s = (int)Math.Round(255.0 * c / Math.Max(1, columns - 1));
                grid[r, c] = new ColorHsv(((hue % 360) + 360) % 360, s, v).ToArgb();
            }
        }

        return grid;
    }

    /// <summary>色相 from を target の方へ、近い回り方で amount 度だけ回す（行き過ぎない）。</summary>
    public static int TurnToward(int from, int target, int amount)
    {
        int diff = ((target - from) % 360 + 540) % 360 - 180; // -180〜179
        int turn = Math.Sign(diff) * Math.Min(Math.Abs(diff), amount);
        return ((from + turn) % 360 + 360) % 360;
    }
}
