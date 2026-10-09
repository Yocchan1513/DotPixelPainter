using System.Buffers.Binary;
using System.Text;

namespace DotPixelPainter.Core;

/// <summary>PSD を読み込んだ結果。そのまま読めなかった部分は Warnings に入る。</summary>
public sealed record PsdImportResult(PixelDocument Document, IReadOnlyList<string> Warnings);

/// <summary>
/// PSD（Photoshop 形式）の読み込み。Adobe が公開している仕様書をもとにした独自実装。
/// 対応: 8bit の RGB / グレースケール、レイヤー（名前・表示・不透明度・位置）、無圧縮と RLE 圧縮、
///       レイヤーのない（統合済みの）PSD。
/// 対応しないもの: 16/32bit、CMYK など、ZIP 圧縮のレイヤー、調整レイヤー、レイヤー効果。
/// 通常以外の描画モードは「通常」として、レイヤーフォルダは外して読み込み、Warnings で知らせる。
/// </summary>
public static class PsdReader
{
    public const string Extension = ".psd";

    private enum Section
    {
        Normal = 0,
        OpenFolder = 1,
        ClosedFolder = 2,
        FolderEnd = 3,
    }

    private sealed class LayerRecord
    {
        public int Top;
        public int Left;
        public int Bottom;
        public int Right;
        public List<(short Id, long Length)> Channels = [];
        public string BlendMode = "norm";
        public byte Opacity = 255;
        public byte Flags;
        public string Name = "";
        public Section Section;

        public bool Hidden => (Flags & 0x02) != 0;

        public int Width => Right - Left;

        public int Height => Bottom - Top;
    }

    public static PsdImportResult Read(ReadOnlySpan<byte> data, string name)
    {
        var r = new Reader(data);
        if (r.Ascii(4) != "8BPS")
        {
            throw new FormatException("PSD ファイルではありません。");
        }

        int version = r.U16();
        if (version != 1)
        {
            throw new FormatException("大きな PSD（PSB 形式）には対応していません。");
        }

        r.Skip(6);
        int channelCount = r.U16();
        int height = r.I32();
        int width = r.I32();
        int depth = r.U16();
        int colorMode = r.U16();

        if (depth != 8)
        {
            throw new FormatException($"{depth}bit の PSD には対応していません（8bit のみ）。Photoshop などで 8bit に変換してから開いてください。");
        }

        if (colorMode is not (1 or 3))
        {
            throw new FormatException("RGB とグレースケール以外の PSD（CMYK など）には対応していません。");
        }

        if (width < 1 || height < 1 || width > PixelImage.MaxSize || height > PixelImage.MaxSize)
        {
            throw new FormatException($"画像の大きさ（{width}×{height}）に対応していません。{PixelImage.MaxSize}×{PixelImage.MaxSize} までです。");
        }

        bool gray = colorMode == 1;
        r.Skip(r.I32()); // カラーモードデータ
        r.Skip(r.I32()); // イメージリソース

        var warnings = new List<string>();
        int layerAndMaskLength = r.I32();
        int layerAndMaskEnd = r.Position + layerAndMaskLength;
        var layers = new List<Layer>();

        if (layerAndMaskLength > 0)
        {
            int layerInfoLength = r.I32();
            if (layerInfoLength > 0)
            {
                int layerInfoEnd = r.Position + layerInfoLength;
                layers = ReadLayers(ref r, width, height, gray, warnings);
                r.Position = layerInfoEnd;
            }
        }

        r.Position = layerAndMaskEnd;

        PixelDocument document;
        if (layers.Count > 0)
        {
            document = PixelDocument.FromLayers(name, width, height, layers, layers.Count - 1);
        }
        else
        {
            // レイヤーがない PSD は、最後の「統合画像」を1枚のレイヤーにする
            PixelImage merged = ReadMergedImage(ref r, width, height, channelCount, gray);
            document = PixelDocument.FromLayers(name, width, height, [new Layer("背景", merged)], 0);
        }

        return new PsdImportResult(document, warnings);
    }

    private static List<Layer> ReadLayers(ref Reader r, int width, int height, bool gray, List<string> warnings)
    {
        int count = Math.Abs((short)r.U16());
        var records = new List<LayerRecord>(count);
        for (int i = 0; i < count; i++)
        {
            records.Add(ReadRecord(ref r));
        }

        // チャンネルの画像は、レイヤー → チャンネルの順に並んでいる
        var images = new List<PixelImage?>(count);
        var unsupported = new HashSet<string>();
        foreach (LayerRecord record in records)
        {
            images.Add(ReadLayerImage(ref r, record, width, height, gray, unsupported));
        }

        // フォルダの表示状態を中の各レイヤーに反映する（並びは下から上。上から順に見ていく）
        var visible = new bool[count];
        var folderVisible = new Stack<bool>();
        for (int i = count - 1; i >= 0; i--)
        {
            LayerRecord rec = records[i];
            bool parentsVisible = folderVisible.All(v => v);
            switch (rec.Section)
            {
                case Section.OpenFolder or Section.ClosedFolder:
                    folderVisible.Push(!rec.Hidden);
                    break;
                case Section.FolderEnd:
                    if (folderVisible.Count > 0)
                    {
                        folderVisible.Pop();
                    }

                    break;
                default:
                    visible[i] = !rec.Hidden && parentsVisible;
                    break;
            }
        }

        var layers = new List<Layer>();
        bool hadFolders = false;
        var blendModes = new HashSet<string>();
        for (int i = 0; i < count; i++)
        {
            LayerRecord rec = records[i];
            if (rec.Section != Section.Normal)
            {
                hadFolders = true;
                continue;
            }

            if (images[i] is not { } image)
            {
                continue;
            }

            if (rec.BlendMode != "norm")
            {
                blendModes.Add(BlendModeName(rec.BlendMode));
            }

            layers.Add(new Layer(rec.Name.Length > 0 ? rec.Name : $"レイヤー {layers.Count + 1}", image)
            {
                Visible = visible[i],
                Opacity = rec.Opacity / 255.0,
            });
        }

        if (hadFolders)
        {
            warnings.Add("レイヤーフォルダはまだ使えないため、フォルダを外してレイヤーだけを読み込みました。");
        }

        if (blendModes.Count > 0)
        {
            warnings.Add($"描画モード（{string.Join("・", blendModes)}）は「通常」として読み込みました。見た目が変わることがあります。");
        }

        foreach (string message in unsupported)
        {
            warnings.Add(message);
        }

        return layers;
    }

    private static LayerRecord ReadRecord(ref Reader r)
    {
        var rec = new LayerRecord
        {
            Top = r.I32(),
            Left = r.I32(),
            Bottom = r.I32(),
            Right = r.I32(),
        };
        int channels = r.U16();
        for (int c = 0; c < channels; c++)
        {
            rec.Channels.Add(((short)r.U16(), r.I32()));
        }

        if (r.Ascii(4) != "8BIM")
        {
            throw new FormatException("PSD のレイヤー情報が壊れています。");
        }

        rec.BlendMode = r.Ascii(4);
        rec.Opacity = r.U8();
        r.Skip(1); // クリッピング
        rec.Flags = r.U8();
        r.Skip(1);

        int extraLength = r.I32();
        int extraEnd = r.Position + extraLength;
        r.Skip(r.I32()); // レイヤーマスク
        r.Skip(r.I32()); // 合成範囲
        int nameLength = r.U8();
        rec.Name = Encoding.Latin1.GetString(r.Bytes(nameLength));
        r.Skip((4 - ((nameLength + 1) % 4)) % 4); // 名前は長さの1バイトと合わせて4の倍数にそろえてある

        // 追加情報: Unicode の名前（luni）とフォルダの区切り（lsct）を読む
        while (r.Position + 12 <= extraEnd)
        {
            string signature = r.Ascii(4);
            if (signature is not ("8BIM" or "8B64"))
            {
                break;
            }

            string key = r.Ascii(4);
            int length = r.I32();
            int end = r.Position + length;
            if (key == "luni" && length >= 4)
            {
                int chars = r.I32();
                rec.Name = Encoding.BigEndianUnicode.GetString(r.Bytes(Math.Min(chars * 2, length - 4))).TrimEnd('\0');
            }
            else if (key is "lsct" or "lsdk" && length >= 4)
            {
                rec.Section = (Section)Math.Clamp(r.I32(), 0, 3);
            }

            r.Position = end + (length % 2); // 偶数にそろえてあることがある
        }

        r.Position = extraEnd;
        return rec;
    }

    /// <summary>1枚のレイヤーの画像を、キャンバスと同じ大きさの画像にして返す。読めないときは null。</summary>
    private static PixelImage? ReadLayerImage(ref Reader r, LayerRecord rec, int width, int height, bool gray, HashSet<string> unsupported)
    {
        int w = rec.Width;
        int h = rec.Height;
        var planes = new Dictionary<short, byte[]>();
        bool ok = true;

        foreach ((short id, long length) in rec.Channels)
        {
            int start = r.Position;
            int end = checked(start + (int)length);
            int compression = r.U16();
            bool isMask = id < -1;
            if (!isMask && w > 0 && h > 0)
            {
                switch (compression)
                {
                    case 0:
                        planes[id] = r.Bytes(w * h).ToArray();
                        break;
                    case 1:
                        planes[id] = ReadRle(ref r, w, h);
                        break;
                    default:
                        unsupported.Add("ZIP 圧縮のレイヤーは読み込めないため、空のレイヤーにしました。");
                        ok = false;
                        break;
                }
            }

            r.Position = end; // 長さどおりに進める（マスクなど読まない分もここで飛ばす）
        }

        var image = new PixelImage(width, height);
        if (!ok || w <= 0 || h <= 0)
        {
            return image;
        }

        byte[]? red = planes.GetValueOrDefault((short)0);
        byte[]? green = gray ? red : planes.GetValueOrDefault((short)1);
        byte[]? blue = gray ? red : planes.GetValueOrDefault((short)2);
        byte[]? alpha = planes.GetValueOrDefault((short)-1);
        for (int y = 0; y < h; y++)
        {
            int cy = rec.Top + y;
            if ((uint)cy >= (uint)height)
            {
                continue;
            }

            for (int x = 0; x < w; x++)
            {
                int cx = rec.Left + x;
                if ((uint)cx >= (uint)width)
                {
                    continue;
                }

                int i = y * w + x;
                uint a = alpha is null ? 255u : alpha[i];
                if (a == 0)
                {
                    continue;
                }

                uint rr = red?[i] ?? 0;
                uint gg = green?[i] ?? 0;
                uint bb = blue?[i] ?? 0;
                image.SetPixel(cx, cy, a << 24 | rr << 16 | gg << 8 | bb);
            }
        }

        return image;
    }

    private static PixelImage ReadMergedImage(ref Reader r, int width, int height, int channelCount, bool gray)
    {
        int compression = r.U16();
        int planeSize = width * height;
        var planes = new byte[channelCount][];
        if (compression == 0)
        {
            for (int c = 0; c < channelCount; c++)
            {
                planes[c] = r.Bytes(planeSize).ToArray();
            }
        }
        else if (compression == 1)
        {
            // 全チャンネル分の行の長さが先にまとめて並ぶ
            var rowLengths = new int[channelCount * height];
            for (int i = 0; i < rowLengths.Length; i++)
            {
                rowLengths[i] = r.U16();
            }

            for (int c = 0; c < channelCount; c++)
            {
                planes[c] = new byte[planeSize];
                for (int y = 0; y < height; y++)
                {
                    PackBits(r.Bytes(rowLengths[c * height + y]), planes[c].AsSpan(y * width, width));
                }
            }
        }
        else
        {
            throw new FormatException("この PSD の統合画像の圧縮形式には対応していません。");
        }

        int colors = gray ? 1 : 3;
        bool hasAlpha = channelCount > colors;
        var image = new PixelImage(width, height);
        for (int i = 0; i < planeSize; i++)
        {
            uint rr = planes[0][i];
            uint gg = gray ? rr : planes[1][i];
            uint bb = gray ? rr : planes[2][i];
            uint a = hasAlpha ? planes[colors][i] : 255u;
            image.SetPixel(i % width, i / width, a << 24 | rr << 16 | gg << 8 | bb);
        }

        return image;
    }

    private static byte[] ReadRle(ref Reader r, int width, int height)
    {
        var rowLengths = new int[height];
        for (int y = 0; y < height; y++)
        {
            rowLengths[y] = r.U16();
        }

        var plane = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            PackBits(r.Bytes(rowLengths[y]), plane.AsSpan(y * width, width));
        }

        return plane;
    }

    /// <summary>PackBits 形式の1行を展開する。</summary>
    internal static void PackBits(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        int s = 0;
        int d = 0;
        while (s < src.Length && d < dst.Length)
        {
            int n = (sbyte)src[s++];
            if (n >= 0)
            {
                int count = Math.Min(n + 1, Math.Min(dst.Length - d, src.Length - s));
                src.Slice(s, count).CopyTo(dst[d..]);
                s += n + 1;
                d += count;
            }
            else if (n != -128 && s < src.Length)
            {
                int count = Math.Min(1 - n, dst.Length - d);
                dst.Slice(d, count).Fill(src[s++]);
                d += count;
            }
        }
    }

    private static string BlendModeName(string key) => key switch
    {
        "mul " => "乗算",
        "scrn" => "スクリーン",
        "over" => "オーバーレイ",
        "lddg" => "覆い焼き（リニア）",
        "dark" => "比較（暗）",
        "lite" => "比較（明）",
        "pass" => "通過",
        _ => key.Trim(),
    };

    /// <summary>ビッグエンディアンで順に読む小さな読み取り器。</summary>
    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public int Position { get; set; }

        public ReadOnlySpan<byte> Bytes(int count)
        {
            if (count < 0 || Position + count > _data.Length)
            {
                throw new FormatException("PSD ファイルが途中で切れています。");
            }

            ReadOnlySpan<byte> slice = _data.Slice(Position, count);
            Position += count;
            return slice;
        }

        public void Skip(int count) => Bytes(count);

        public byte U8() => Bytes(1)[0];

        public int U16() => BinaryPrimitives.ReadUInt16BigEndian(Bytes(2));

        public int I32() => BinaryPrimitives.ReadInt32BigEndian(Bytes(4));

        public string Ascii(int count) => Encoding.ASCII.GetString(Bytes(count));
    }
}
