namespace DotPixelPainter.Core;

/// <summary>画素単位の四角形（X, Y が左上、幅と高さは 1 以上）。</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    /// <summary>2点（どちらも含む）を囲む四角形。向きは問わない。</summary>
    public static PixelRect FromPoints(int x0, int y0, int x1, int y1) =>
        new(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0) + 1, Math.Abs(y1 - y0) + 1);

    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    /// <summary>重なる部分。重ならなければ null。</summary>
    public PixelRect? Intersect(PixelRect other)
    {
        int x = Math.Max(X, other.X);
        int y = Math.Max(Y, other.Y);
        int r = Math.Min(Right, other.Right);
        int b = Math.Min(Bottom, other.Bottom);
        return r > x && b > y ? new PixelRect(x, y, r - x, b - y) : null;
    }

    public PixelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };
}

/// <summary>
/// 持ち上げた選択範囲の中身。持ち上げると元の場所は透明になり、
/// 動かしたあと Drop で新しい場所に置く。持ち上げから置くまでを1つの PixelStroke で記録するので、
/// 移動全体が1回の「元に戻す」で戻る。
/// </summary>
public sealed class FloatingSelection
{
    private uint[] _pixels;

    private FloatingSelection(PixelRect bounds, uint[] pixels)
    {
        Bounds = bounds;
        _pixels = pixels;
    }

    /// <summary>今の位置と大きさ（画像の外にはみ出してもよい）。</summary>
    public PixelRect Bounds { get; private set; }

    public ReadOnlySpan<uint> Pixels => _pixels;

    /// <summary>貼り付け用。image の中身を (x, y) に浮かせる（元の画像は変えない）。</summary>
    public static FloatingSelection FromImage(PixelImage image, int x, int y) =>
        new(new PixelRect(x, y, image.Width, image.Height), image.Pixels.ToArray());

    /// <summary>コピー用に、中身を1枚の画像として取り出す。</summary>
    public PixelImage ToImage()
    {
        var image = new PixelImage(Bounds.Width, Bounds.Height);
        for (int i = 0; i < _pixels.Length; i++)
        {
            image.SetPixel(i % Bounds.Width, i / Bounds.Width, _pixels[i]);
        }

        return image;
    }

    public void FlipHorizontal()
    {
        int w = Bounds.Width;
        for (int y = 0; y < Bounds.Height; y++)
        {
            Array.Reverse(_pixels, y * w, w);
        }
    }

    public void FlipVertical()
    {
        int w = Bounds.Width;
        int h = Bounds.Height;
        for (int y = 0; y < h / 2; y++)
        {
            Span<uint> a = _pixels.AsSpan(y * w, w);
            Span<uint> b = _pixels.AsSpan((h - 1 - y) * w, w);
            for (int x = 0; x < w; x++)
            {
                (a[x], b[x]) = (b[x], a[x]);
            }
        }
    }

    /// <summary>90度回す。幅と高さが入れ替わるので、中心がなるべく動かないように位置を合わせる。</summary>
    public void Rotate90(bool clockwise)
    {
        int w = Bounds.Width;
        int h = Bounds.Height;
        var rotated = new uint[_pixels.Length];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // 回したあとの幅は h、高さは w
                (int nx, int ny) = clockwise ? (h - 1 - y, x) : (y, w - 1 - x);
                rotated[ny * h + nx] = _pixels[y * w + x];
            }
        }

        _pixels = rotated;
        Bounds = new PixelRect(Bounds.X + (w - h) / 2, Bounds.Y + (h - w) / 2, h, w);
    }

    /// <summary>
    /// rect の中身を image から持ち上げ、元の場所を stroke で透明にする。
    /// rect が画像と重ならなければ null。
    /// </summary>
    public static FloatingSelection? Lift(PixelStroke stroke, PixelImage image, PixelRect rect)
    {
        if (rect.Intersect(new PixelRect(0, 0, image.Width, image.Height)) is not { } area)
        {
            return null;
        }

        var pixels = new uint[area.Width * area.Height];
        for (int y = 0; y < area.Height; y++)
        {
            for (int x = 0; x < area.Width; x++)
            {
                pixels[y * area.Width + x] = image.GetPixel(area.X + x, area.Y + y);
                stroke.Plot(area.X + x, area.Y + y, 0);
            }
        }

        return new FloatingSelection(area, pixels);
    }

    public void MoveBy(int dx, int dy) => Bounds = Bounds.Offset(dx, dy);

    /// <summary>今の位置に置く。透明な画素は下の絵を消さない。画像の外にはみ出した分は捨てる。</summary>
    public void Drop(PixelStroke stroke)
    {
        for (int y = 0; y < Bounds.Height; y++)
        {
            for (int x = 0; x < Bounds.Width; x++)
            {
                uint c = _pixels[y * Bounds.Width + x];
                if (c >> 24 != 0)
                {
                    stroke.Plot(Bounds.X + x, Bounds.Y + y, c);
                }
            }
        }
    }

    /// <summary>表示用に、この中身を image の上に重ねる（image は書き換わる）。</summary>
    public void DrawOnto(PixelImage image)
    {
        for (int y = 0; y < Bounds.Height; y++)
        {
            for (int x = 0; x < Bounds.Width; x++)
            {
                uint c = _pixels[y * Bounds.Width + x];
                if (c >> 24 != 0)
                {
                    image.SetPixel(Bounds.X + x, Bounds.Y + y, c);
                }
            }
        }
    }
}
