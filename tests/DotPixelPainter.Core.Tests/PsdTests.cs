using System.Buffers.Binary;
using System.Text;
using DotPixelPainter.Core;

namespace DotPixelPainter.Core.Tests;

public class PsdTests
{
    /// <summary>テスト用のレイヤー。Pixels は Width×Height の 0xAARRGGBB。</summary>
    private sealed record TestLayer(string Name, int Left, int Top, int Width, int Height, uint[] Pixels)
    {
        public string? UnicodeName { get; init; }

        public bool Hidden { get; init; }

        public byte Opacity { get; init; } = 255;

        public string Blend { get; init; } = "norm";

        public int Section { get; init; }
    }

    /// <summary>仕様どおりの PSD を組み立てる（テスト専用の最小限の書き出し器）。</summary>
    private static byte[] BuildPsd(int width, int height, IReadOnlyList<TestLayer> layers, bool rle = false, bool gray = false, uint[]? merged = null, int depth = 8)
    {
        var o = new List<byte>();
        void U8(int v) => o.Add((byte)v);
        void U16(int v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)v); o.AddRange(b); }
        void I32(int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, v); o.AddRange(b); }
        void Ascii(string s) => o.AddRange(Encoding.ASCII.GetBytes(s));
        void PatchI32(int at, int v) => BinaryPrimitives.WriteInt32BigEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(o)[at..], v);

        int colors = gray ? 1 : 3;
        Ascii("8BPS"); U16(1); o.AddRange(new byte[6]); U16(colors); I32(height); I32(width); U16(depth); U16(gray ? 1 : 3);
        I32(0); // カラーモードデータ
        I32(0); // イメージリソース

        int layerAndMaskAt = o.Count; I32(0);
        if (layers.Count > 0)
        {
            int layerInfoAt = o.Count; I32(0);
            U16(layers.Count);
            short[] ids = gray ? [-1, 0] : [-1, 0, 1, 2];
            var channelData = new List<byte[]>();
            foreach (TestLayer l in layers)
            {
                I32(l.Top); I32(l.Left); I32(l.Top + l.Height); I32(l.Left + l.Width);
                U16(ids.Length);
                foreach (short id in ids)
                {
                    byte[] plane = Plane(l, id);
                    byte[] encoded = Encode(plane, l.Width, l.Height, rle);
                    channelData.Add(encoded);
                    U16((ushort)id); I32(encoded.Length);
                }

                Ascii("8BIM"); Ascii(l.Blend); U8(l.Opacity); U8(0); U8(l.Hidden ? 0x02 : 0); U8(0);
                int extraAt = o.Count; I32(0);
                I32(0); I32(0); // マスク・合成範囲
                byte[] name = Encoding.ASCII.GetBytes(l.Name);
                U8(name.Length); o.AddRange(name);
                while ((name.Length + 1 + (o.Count - extraAt - 4 - 8 - 1 - name.Length)) % 4 != 0) { U8(0); }
                if (l.UnicodeName is { } u)
                {
                    byte[] text = Encoding.BigEndianUnicode.GetBytes(u);
                    Ascii("8BIM"); Ascii("luni"); I32(4 + text.Length); I32(u.Length); o.AddRange(text);
                }

                if (l.Section != 0)
                {
                    Ascii("8BIM"); Ascii("lsct"); I32(4); I32(l.Section);
                }

                PatchI32(extraAt, o.Count - extraAt - 4);
            }

            foreach (byte[] data in channelData)
            {
                o.AddRange(data);
            }

            if ((o.Count - layerInfoAt) % 2 != 0) { U8(0); }
            PatchI32(layerInfoAt, o.Count - layerInfoAt - 4);
            I32(0); // 全体のマスク情報
        }

        PatchI32(layerAndMaskAt, o.Count - layerAndMaskAt - 4);

        // 統合画像（無圧縮）
        U16(0);
        merged ??= new uint[width * height];
        for (int c = 0; c < colors; c++)
        {
            int shift = gray ? 16 : 16 - c * 8;
            foreach (uint p in merged) { U8((int)((p >> shift) & 0xFF)); }
        }

        return [.. o];
    }

    private static byte[] Plane(TestLayer l, short id)
    {
        int shift = id switch { -1 => 24, 0 => 16, 1 => 8, _ => 0 };
        return [.. l.Pixels.Select(p => (byte)(p >> shift))];
    }

    private static byte[] Encode(byte[] plane, int width, int height, bool rle)
    {
        var o = new List<byte>();
        if (!rle)
        {
            o.Add(0); o.Add(0);
            o.AddRange(plane);
            return [.. o];
        }

        o.Add(0); o.Add(1);
        var rows = new List<byte[]>();
        for (int y = 0; y < height; y++)
        {
            rows.Add(PackBitsEncode(plane.AsSpan(y * width, width)));
        }

        foreach (byte[] row in rows) { o.Add((byte)(row.Length >> 8)); o.Add((byte)row.Length); }
        foreach (byte[] row in rows) { o.AddRange(row); }
        return [.. o];
    }

    /// <summary>同じ値が3つ以上続くところは繰り返し、それ以外はそのまま並べる。</summary>
    private static byte[] PackBitsEncode(ReadOnlySpan<byte> row)
    {
        var o = new List<byte>();
        int i = 0;
        while (i < row.Length)
        {
            int run = 1;
            while (i + run < row.Length && row[i + run] == row[i] && run < 128) { run++; }
            if (run >= 3)
            {
                o.Add((byte)(sbyte)(1 - run)); o.Add(row[i]); i += run;
                continue;
            }

            int start = i;
            while (i < row.Length && i - start < 128 && !(i + 2 < row.Length && row[i] == row[i + 1] && row[i] == row[i + 2])) { i++; }
            o.Add((byte)(i - start - 1));
            for (int k = start; k < i; k++) { o.Add(row[k]); }
        }

        return [.. o];
    }

    private static uint[] Filled(int count, uint color) => Enumerable.Repeat(color, count).ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Layers_AreReadWithPositionNameVisibilityOpacity(bool rle)
    {
        TestLayer bottom = new("bg", 0, 0, 4, 4, Filled(16, 0xFF112233));
        TestLayer top = new("dot", 2, 1, 2, 2, [0xFFFF0000, 0x00000000, 0x80FFFFFF, 0xFF00FF00]) { Hidden = true, Opacity = 128 };

        PsdImportResult result = PsdReader.Read(BuildPsd(4, 4, [bottom, top], rle), "a.psd");
        PixelDocument doc = result.Document;

        Assert.Equal(["bg", "dot"], doc.Layers.Select(l => l.Name));
        Assert.Equal(0xFF112233u, doc.Layers[0].Image.GetPixel(3, 3));
        Assert.Equal(0xFFFF0000u, doc.Layers[1].Image.GetPixel(2, 1)); // 位置 (2,1) に置かれる
        Assert.Equal(0u, doc.Layers[1].Image.GetPixel(3, 1));          // 透明
        Assert.Equal(0x80FFFFFFu, doc.Layers[1].Image.GetPixel(2, 2));
        Assert.Equal(0u, doc.Layers[1].Image.GetPixel(0, 0));          // 範囲外は透明
        Assert.False(doc.Layers[1].Visible);
        Assert.Equal(128 / 255.0, doc.Layers[1].Opacity, 3);
        Assert.Empty(result.Warnings);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void UnicodeName_IsPreferred()
    {
        TestLayer layer = new("line", 0, 0, 1, 1, [0xFF000000]) { UnicodeName = "線画" };

        Assert.Equal("線画", PsdReader.Read(BuildPsd(1, 1, [layer]), "a.psd").Document.Layers[0].Name);
    }

    [Fact]
    public void HiddenFolder_HidesItsLayers_AndFoldersAreRemoved()
    {
        // 並びは下から: [フォルダの終わり] [中のレイヤー] [フォルダ（非表示）] [外のレイヤー]
        TestLayer end = new("</Layer group>", 0, 0, 0, 0, []) { Section = 3 };
        TestLayer inner = new("inner", 0, 0, 1, 1, [0xFF0000FF]);
        TestLayer folder = new("folder", 0, 0, 0, 0, []) { Section = 1, Hidden = true };
        TestLayer outer = new("outer", 0, 0, 1, 1, [0xFFFF0000]);

        PsdImportResult result = PsdReader.Read(BuildPsd(1, 1, [end, inner, folder, outer]), "a.psd");

        Assert.Equal(["inner", "outer"], result.Document.Layers.Select(l => l.Name));
        Assert.False(result.Document.Layers[0].Visible);
        Assert.True(result.Document.Layers[1].Visible);
        Assert.Contains(result.Warnings, w => w.Contains("フォルダ"));
    }

    [Fact]
    public void OtherBlendMode_IsReadAsNormal_WithWarning()
    {
        TestLayer layer = new("shade", 0, 0, 1, 1, [0xFF808080]) { Blend = "mul " };

        PsdImportResult result = PsdReader.Read(BuildPsd(1, 1, [layer]), "a.psd");

        Assert.Single(result.Document.Layers);
        Assert.Contains(result.Warnings, w => w.Contains("乗算"));
    }

    [Fact]
    public void FlattenedPsd_UsesMergedImage()
    {
        uint[] merged = [0xFF102030, 0xFF405060, 0xFF708090, 0xFFA0B0C0];

        PixelDocument doc = PsdReader.Read(BuildPsd(2, 2, [], merged: merged), "a.psd").Document;

        Assert.Single(doc.Layers);
        Assert.Equal(merged, doc.Layers[0].Image.Pixels.ToArray());
    }

    [Fact]
    public void Grayscale_IsReadAsGray()
    {
        TestLayer layer = new("g", 0, 0, 1, 1, [0xFF7F7F7F]);

        PixelDocument doc = PsdReader.Read(BuildPsd(1, 1, [layer], gray: true), "a.psd").Document;

        Assert.Equal(0xFF7F7F7Fu, doc.Layers[0].Image.GetPixel(0, 0));
    }

    [Fact]
    public void NotPsd_And16Bit_AreRejectedWithMessages()
    {
        Assert.Throws<FormatException>(() => PsdReader.Read(Encoding.ASCII.GetBytes("hello world, not a psd at all"), "x"));

        var ex = Assert.Throws<FormatException>(() => PsdReader.Read(BuildPsd(1, 1, [], depth: 16), "x"));
        Assert.Contains("16bit", ex.Message);
    }

    [Fact]
    public void TruncatedFile_IsRejected()
    {
        TestLayer layer = new("a", 0, 0, 4, 4, Filled(16, 0xFFFFFFFF));
        byte[] psd = BuildPsd(4, 4, [layer]);

        Assert.Throws<FormatException>(() => PsdReader.Read(psd.AsSpan(0, psd.Length / 2).ToArray(), "x"));
    }

    [Fact]
    public void PackBits_HandlesLiteralRunAndNoop()
    {
        // リテラル3つ（1,2,3）、何もしない（-128）、5回繰り返し（9）
        byte[] src = [2, 1, 2, 3, 0x80, unchecked((byte)-4), 9];
        var dst = new byte[8];

        PsdReader.PackBits(src, dst);

        Assert.Equal([1, 2, 3, 9, 9, 9, 9, 9], dst);
    }
}
