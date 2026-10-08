namespace DotPixelPainter.Core;

/// <summary>
/// 1枚のRGBA画像。色は 0xAARRGGBB（ストレートアルファ）の uint で持つ。
/// </summary>
public sealed class PixelImage
{
    public const int MaxSize = 4096;

    private readonly uint[] _pixels;

    public PixelImage(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaxSize || height > MaxSize)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"画像サイズは 1〜{MaxSize} の範囲で指定してください。");
        }

        Width = width;
        Height = height;
        _pixels = new uint[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlySpan<uint> Pixels => _pixels;

    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public uint GetPixel(int x, int y) => Contains(x, y) ? _pixels[y * Width + x] : 0;

    /// <summary>範囲外なら何もしない。色が変わったときだけ true を返す。</summary>
    public bool SetPixel(int x, int y, uint argb)
    {
        if (!Contains(x, y))
        {
            return false;
        }

        ref uint p = ref _pixels[y * Width + x];
        if (p == argb)
        {
            return false;
        }

        p = argb;
        return true;
    }

    public void Fill(uint argb) => Array.Fill(_pixels, argb);

    public PixelImage Clone()
    {
        var copy = new PixelImage(Width, Height);
        _pixels.CopyTo(copy._pixels, 0);
        return copy;
    }

    /// <summary>外部から読み込んだ BGRA（ストレートアルファ）バイト列で中身を置き換える。</summary>
    public void LoadFromBgra(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length != _pixels.Length * 4)
        {
            throw new ArgumentException("バイト列の長さが画像サイズと合いません。", nameof(bgra));
        }

        for (int i = 0; i < _pixels.Length; i++)
        {
            int o = i * 4;
            _pixels[i] = (uint)(bgra[o + 3] << 24 | bgra[o + 2] << 16 | bgra[o + 1] << 8 | bgra[o]);
        }
    }

    /// <summary>BGRA（ストレートアルファ）のバイト列に書き出す。PNG保存用。</summary>
    public void CopyToBgra(Span<byte> destination)
    {
        if (destination.Length != _pixels.Length * 4)
        {
            throw new ArgumentException("書き出し先の長さが画像サイズと合いません。", nameof(destination));
        }

        for (int i = 0; i < _pixels.Length; i++)
        {
            uint c = _pixels[i];
            int o = i * 4;
            destination[o] = (byte)c;
            destination[o + 1] = (byte)(c >> 8);
            destination[o + 2] = (byte)(c >> 16);
            destination[o + 3] = (byte)(c >> 24);
        }
    }

    /// <summary>BGRA（乗算済みアルファ）のバイト列に書き出す。画面表示用。</summary>
    public void CopyToBgraPremultiplied(Span<byte> destination)
    {
        if (destination.Length != _pixels.Length * 4)
        {
            throw new ArgumentException("書き出し先の長さが画像サイズと合いません。", nameof(destination));
        }

        for (int i = 0; i < _pixels.Length; i++)
        {
            uint c = _pixels[i];
            uint a = c >> 24;
            int o = i * 4;
            destination[o] = (byte)(((c & 0xFF) * a + 127) / 255);
            destination[o + 1] = (byte)((((c >> 8) & 0xFF) * a + 127) / 255);
            destination[o + 2] = (byte)((((c >> 16) & 0xFF) * a + 127) / 255);
            destination[o + 3] = (byte)a;
        }
    }
}
