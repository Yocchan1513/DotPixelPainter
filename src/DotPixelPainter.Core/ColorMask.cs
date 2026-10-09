namespace DotPixelPainter.Core;

public enum ColorMaskMode
{
    /// <summary>マスクなし。</summary>
    Off,

    /// <summary>指定した色の上には描かない（線画を守って塗るときなど）。</summary>
    Protect,

    /// <summary>指定した色の上にだけ描く（肌の部分だけに影を入れるときなど）。</summary>
    Only,
}

/// <summary>
/// カラーマスク（AzPainter2 の「マスク／逆マスク」にあたる）。描く前の色を見て、その画素に描いてよいかを決める。
/// 完全に透明な色は、RGB が違っても同じ「透明」として扱う。
/// </summary>
public sealed class ColorMask
{
    public const int MaxColors = 8;

    private readonly List<uint> _colors = [];

    public ColorMaskMode Mode { get; set; }

    public IReadOnlyList<uint> Colors => _colors;

    public bool IsActive => Mode != ColorMaskMode.Off && _colors.Count > 0;

    public bool Add(uint argb)
    {
        uint c = Normalize(argb);
        if (_colors.Contains(c) || _colors.Count >= MaxColors)
        {
            return false;
        }

        _colors.Add(c);
        return true;
    }

    public void Remove(uint argb) => _colors.Remove(Normalize(argb));

    public void Clear() => _colors.Clear();

    /// <summary>描く前の色が existing の画素に、描いてよいか。</summary>
    public bool Allows(uint existing)
    {
        if (!IsActive)
        {
            return true;
        }

        bool listed = _colors.Contains(Normalize(existing));
        return Mode == ColorMaskMode.Protect ? !listed : listed;
    }

    private static uint Normalize(uint argb) => argb >> 24 == 0 ? 0u : argb;
}
