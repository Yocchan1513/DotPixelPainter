namespace DotPixelPainter.Core;

/// <summary>1行ぶんの連続した画素（X0〜X1 を含む）。</summary>
public readonly record struct PixelSpan(int Y, int X0, int X1);

/// <summary>
/// 直線・四角・円をピクセル単位で求める。結果は行ごとの区間（PixelSpan）で返すので、
/// 塗りつぶした大きな図形でもプレビュー描画が軽い。
/// </summary>
public static class ShapeRaster
{
    public static List<PixelSpan> Line(int x0, int y0, int x1, int y1) =>
        ToSpans(PixelLine.Enumerate(x0, y0, x1, y1));

    public static List<PixelSpan> Rectangle(int x0, int y0, int x1, int y1, bool filled)
    {
        Order(ref x0, ref x1);
        Order(ref y0, ref y1);
        var spans = new List<PixelSpan>(y1 - y0 + 1);
        for (int y = y0; y <= y1; y++)
        {
            if (filled || y == y0 || y == y1)
            {
                spans.Add(new PixelSpan(y, x0, x1));
            }
            else
            {
                spans.Add(new PixelSpan(y, x0, x0));
                if (x1 != x0)
                {
                    spans.Add(new PixelSpan(y, x1, x1));
                }
            }
        }

        return spans;
    }

    /// <summary>
    /// 四角に内接する楕円（A. Zingl のブレゼンハム型アルゴリズム）。
    /// 塗りつぶしは、輪郭の各行の左端から右端までを埋める。
    /// </summary>
    public static List<PixelSpan> Ellipse(int x0, int y0, int x1, int y1, bool filled)
    {
        Order(ref x0, ref x1);
        Order(ref y0, ref y1);
        var points = new List<(int X, int Y)>();

        long a = x1 - x0;
        long b = y1 - y0;
        long b1 = b & 1;
        long dx = 4 * (1 - a) * b * b;
        long dy = 4 * (b1 + 1) * a * a;
        long err = dx + dy + b1 * a * a;

        int top = y0;
        int bottom = y1;
        y0 += (int)((b + 1) / 2);
        y1 = y0 - (int)b1;
        long aa8 = 8 * a * a;
        long bb8 = 8 * b * b;

        do
        {
            points.Add((x1, y0));
            points.Add((x0, y0));
            points.Add((x0, y1));
            points.Add((x1, y1));
            long e2 = 2 * err;
            if (e2 <= dy)
            {
                y0++;
                y1--;
                dy += aa8;
                err += dy;
            }

            if (e2 >= dx || 2 * err > dy)
            {
                x0++;
                x1--;
                dx += bb8;
                err += dx;
            }
        }
        while (x0 <= x1);

        // 幅がとても狭い楕円の先端を補う
        while (y0 - y1 <= b)
        {
            points.Add((x0 - 1, y0));
            points.Add((x1 + 1, y0++));
            points.Add((x0 - 1, y1));
            points.Add((x1 + 1, y1--));
        }

        points.RemoveAll(p => p.Y < top || p.Y > bottom);
        return filled ? FillRows(points) : ToSpans(points);
    }

    /// <summary>Shift を押しているときの補正。四角・円は正方形に、直線は水平・垂直・45度にそろえる。</summary>
    public static (int X, int Y) Constrain(int x0, int y0, int x1, int y1, bool isLine)
    {
        int dx = x1 - x0;
        int dy = y1 - y0;
        int adx = Math.Abs(dx);
        int ady = Math.Abs(dy);

        if (isLine)
        {
            if (adx > 2 * ady)
            {
                return (x1, y0);
            }

            if (ady > 2 * adx)
            {
                return (x0, y1);
            }
        }

        int d = Math.Max(adx, ady);
        return (x0 + (dx < 0 ? -d : d), y0 + (dy < 0 ? -d : d));
    }

    /// <summary>点の集まりを、行ごとの連続区間にまとめる（重複は1つにする）。</summary>
    public static List<PixelSpan> ToSpans(IEnumerable<(int X, int Y)> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        var spans = new List<PixelSpan>();
        int i = 0;
        while (i < sorted.Count)
        {
            int y = sorted[i].Y;
            int start = sorted[i].X;
            int end = start;
            i++;
            while (i < sorted.Count && sorted[i].Y == y && sorted[i].X == end + 1)
            {
                end++;
                i++;
            }

            spans.Add(new PixelSpan(y, start, end));
        }

        return spans;
    }

    private static List<PixelSpan> FillRows(List<(int X, int Y)> points) =>
        points
            .GroupBy(p => p.Y)
            .OrderBy(g => g.Key)
            .Select(g => new PixelSpan(g.Key, g.Min(p => p.X), g.Max(p => p.X)))
            .ToList();

    private static void Order(ref int a, ref int b)
    {
        if (a > b)
        {
            (a, b) = (b, a);
        }
    }
}
