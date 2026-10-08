namespace DotPixelPainter.Core;

public static class PixelLine
{
    /// <summary>
    /// 2点間を隙間なく結ぶピクセル座標を列挙する（ブレゼンハム）。
    /// ペンを速く動かしたときに点が飛ばないようにするために使う。
    /// </summary>
    public static IEnumerable<(int X, int Y)> Enumerate(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            yield return (x0, y0);
            if (x0 == x1 && y0 == y1)
            {
                yield break;
            }

            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }
}
