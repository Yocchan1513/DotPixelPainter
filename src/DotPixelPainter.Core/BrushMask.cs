namespace DotPixelPainter.Core;

/// <summary>
/// ペン先の形。中心からのずれ（Dx, Dy）の一覧で表す。
/// サイズ 1〜2 は丸でも四角になる（ドット絵ではそれが自然なため）。
/// </summary>
public sealed class BrushMask
{
    public const int MaxSize = 16;

    private static readonly Dictionary<(int, bool), BrushMask> Cache = [];

    private BrushMask(int size, bool round, (int Dx, int Dy)[] offsets)
    {
        Size = size;
        Round = round;
        Offsets = offsets;
    }

    public int Size { get; }

    public bool Round { get; }

    public IReadOnlyList<(int Dx, int Dy)> Offsets { get; }

    public static BrushMask Single { get; } = Create(1, false);

    public static BrushMask Create(int size, bool round)
    {
        size = Math.Clamp(size, 1, MaxSize);
        lock (Cache)
        {
            if (Cache.TryGetValue((size, round), out BrushMask? cached))
            {
                return cached;
            }

            var offsets = new List<(int, int)>();
            double c = (size - 1) / 2.0;
            double r2 = (size / 2.0) * (size / 2.0) - 0.5;
            int shift = (size - 1) / 2;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool inside = !round || size <= 2 || (x - c) * (x - c) + (y - c) * (y - c) <= r2;
                    if (inside)
                    {
                        offsets.Add((x - shift, y - shift));
                    }
                }
            }

            var mask = new BrushMask(size, round, [.. offsets]);
            Cache[(size, round)] = mask;
            return mask;
        }
    }

    /// <summary>点の並び（線の芯）にペン先を押していった結果の画素を返す。</summary>
    public IEnumerable<(int X, int Y)> Stamp(IEnumerable<(int X, int Y)> points)
    {
        if (Size == 1)
        {
            return points;
        }

        return points.SelectMany(p => Offsets.Select(o => (p.X + o.Dx, p.Y + o.Dy)));
    }
}
