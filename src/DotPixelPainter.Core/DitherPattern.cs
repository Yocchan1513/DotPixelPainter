namespace DotPixelPainter.Core;

/// <summary>
/// ディザ（網かけ）のパターン。画像の座標で決まるので、何回に分けて塗っても模様がずれない。
/// </summary>
public sealed class DitherPattern
{
    private readonly bool[] _cells;

    private DitherPattern(string name, int width, int height, params int[] cells)
    {
        Name = name;
        Width = width;
        Height = height;
        _cells = cells.Select(c => c != 0).ToArray();
    }

    public string Name { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>用意してあるパターン（濃いものから順）。</summary>
    public static IReadOnlyList<DitherPattern> Presets { get; } =
    [
        new("75%", 2, 2,
            1, 1,
            0, 1),
        new("市松 50%", 2, 2,
            1, 0,
            0, 1),
        new("25%", 2, 2,
            1, 0,
            0, 0),
        new("12.5%", 4, 4,
            1, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 0),
        new("6.25%", 4, 4,
            1, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0),
        new("横線", 1, 2,
            1,
            0),
        new("縦線", 2, 1,
            1, 0),
        new("斜線", 4, 4,
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1),
    ];

    /// <summary>(x, y) が塗る画素か。負の座標でも模様が続くようにする。</summary>
    public bool On(int x, int y)
    {
        int px = ((x % Width) + Width) % Width;
        int py = ((y % Height) + Height) % Height;
        return _cells[py * Width + px];
    }
}

/// <summary>パターンで塗るときの設定。GapColor があれば、模様のすき間をその色で塗る（なければすき間はそのまま）。</summary>
public sealed record DrawPattern(DitherPattern Pattern, uint? GapColor = null)
{
    /// <summary>(x, y) に塗る色。塗らない画素なら null。</summary>
    public uint? ColorAt(int x, int y, uint color) => Pattern.On(x, y) ? color : GapColor;
}
