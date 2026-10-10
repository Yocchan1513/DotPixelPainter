namespace DotPixelPainter.Core;

/// <summary>
/// ペンで描いている道筋から、1px の線の角にできる余分な1ドット（L字の真ん中）を見つける。
/// Aseprite などの「ピクセルパーフェクト」と同じ考え方。
/// 例: (0,0) → (1,0) → (1,1) と進んだら、(0,0) と (1,1) は斜めにつながるので (1,0) は要らない。
/// </summary>
public sealed class PixelPerfectPath
{
    private (int X, int Y)? _older;
    private (int X, int Y)? _last;

    /// <summary>次の点を足す。消すべき点があればそれを返す。</summary>
    public (int X, int Y)? Add(int x, int y)
    {
        var point = (x, y);
        if (_last == point)
        {
            return null;
        }

        (int X, int Y)? removed = null;
        if (_older is { } a && _last is { } b
            && IsOrthogonalNeighbor(a, b) && IsOrthogonalNeighbor(b, point)
            && Math.Abs(a.X - x) == 1 && Math.Abs(a.Y - y) == 1)
        {
            // b を飛ばして a と今の点を斜めにつなぐ。次の判定は a からにする
            removed = b;
            _last = point;
            return removed;
        }

        _older = _last;
        _last = point;
        return removed;
    }

    private static bool IsOrthogonalNeighbor((int X, int Y) a, (int X, int Y) b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;
}

/// <summary>
/// すでに描いてある 1px の線から、角にできる余分な1ドットを消す（線の整形）。
/// 消すのは次のすべてに当てはまる画素だけ:
///   - 横と縦に同じ色の画素があり、その2つが斜めにつながっている（L字の角）
///   - 角の内側は同じ色ではない（塗りの角ではない）
///   - 横と縦のどちらの腕もまっすぐ2画素以上は続いていない（四角の角のような、本当の角ではない）
///   - まわりの同じ色が3つ以下で、消しても線が途切れない
/// 消した画素は、角の外側の色（外側の2画素が同じ色のとき。片方が画像の外ならもう片方）にする。透明なレイヤーなら透明になる。
/// </summary>
public static class LineCleanup
{
    /// <summary>image の area の中を整える（image を直接書き換える）。変えた画素数を返す。</summary>
    public static int Run(PixelImage image, PixelRect area)
    {
        int changed = 0;
        for (int y = area.Y; y < area.Bottom; y++)
        {
            for (int x = area.X; x < area.Right; x++)
            {
                uint c = image.GetPixel(x, y);
                if (c >> 24 != 0 && TryReplacement(image, x, y, c) is { } replacement)
                {
                    image.SetPixel(x, y, replacement);
                    changed++;
                }
            }
        }

        return changed;
    }

    private static uint? TryReplacement(PixelImage image, int x, int y, uint c)
    {
        bool Same(int px, int py) => image.Contains(px, py) && image.GetPixel(px, py) == c;

        int neighbors = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx != 0 || dy != 0) && Same(x + dx, y + dy))
                {
                    neighbors++;
                }
            }
        }

        if (neighbors is < 2 or > 3)
        {
            return null;
        }

        foreach (int dx in (ReadOnlySpan<int>)[-1, 1])
        {
            foreach (int dy in (ReadOnlySpan<int>)[-1, 1])
            {
                if (!Same(x + dx, y) || !Same(x, y + dy) || Same(x + dx, y + dy))
                {
                    continue;
                }

                // 四角の角のように、どちらの腕もまっすぐ続いているなら本当の角
                if (Same(x + 2 * dx, y) && Same(x, y + 2 * dy))
                {
                    continue;
                }

                if (!StaysConnectedWithout(Same, x, y))
                {
                    continue;
                }

                // 角の外側の2画素が同じ色なら、その色で埋める。片方が画像の外なら、もう片方の色を使う
                uint? outer1 = image.Contains(x - dx, y) ? image.GetPixel(x - dx, y) : null;
                uint? outer2 = image.Contains(x, y - dy) ? image.GetPixel(x, y - dy) : null;
                uint fill = (outer1, outer2) switch
                {
                    ({ } a, { } b) when a == b => a,
                    ({ } a, null) => a,
                    (null, { } b) => b,
                    (null, null) => 0u,
                    _ => c, // 外側の色がそろっていない: 何で埋めるか決められないので消さない
                };
                if (fill != c)
                {
                    return fill;
                }
            }
        }

        return null;
    }

    /// <summary>(x, y) を除いても、まわり8画素のうち同じ色のものどうしが（斜めも含めて）つながっているか。</summary>
    private static bool StaysConnectedWithout(Func<int, int, bool> same, int x, int y)
    {
        var cells = new List<(int X, int Y)>();
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx != 0 || dy != 0) && same(x + dx, y + dy))
                {
                    cells.Add((x + dx, y + dy));
                }
            }
        }

        var reached = new HashSet<(int, int)> { cells[0] };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue(cells[0]);
        while (queue.Count > 0)
        {
            (int cx, int cy) = queue.Dequeue();
            foreach ((int X, int Y) other in cells)
            {
                if (!reached.Contains(other) && Math.Abs(other.X - cx) <= 1 && Math.Abs(other.Y - cy) <= 1)
                {
                    reached.Add(other);
                    queue.Enqueue(other);
                }
            }
        }

        return reached.Count == cells.Count;
    }
}
