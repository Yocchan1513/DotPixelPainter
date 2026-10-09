using System.Buffers.Binary;
using System.IO.Compression;

namespace DotPixelPainter.Core;

/// <summary>
/// .dotpix の中のレイヤー画像用の、小さな PNG の読み書き。
/// 書き出しは 8bit RGBA。読み込みは 8bit の RGBA / RGB / グレー / グレー+A（インターレースなし）に対応する。
/// 外部ライブラリを使わず、圧縮は .NET 標準の ZLibStream を使う。
/// </summary>
public static class PngCodec
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(PixelImage image) => Encode(image, filter: 0);

    /// <summary>filter はテスト用（0=なし、1=Sub、2=Up、3=Average、4=Paeth）。</summary>
    internal static byte[] Encode(PixelImage image, byte filter)
    {
        int w = image.Width;
        int h = image.Height;
        int stride = w * 4;
        var raw = new byte[h * (stride + 1)];
        var prev = new byte[stride];
        var line = new byte[stride];
        var pixels = image.Pixels;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                uint c = pixels[y * w + x];
                line[x * 4] = (byte)(c >> 16);
                line[x * 4 + 1] = (byte)(c >> 8);
                line[x * 4 + 2] = (byte)c;
                line[x * 4 + 3] = (byte)(c >> 24);
            }

            int o = y * (stride + 1);
            raw[o] = filter;
            for (int i = 0; i < stride; i++)
            {
                byte a = i >= 4 ? line[i - 4] : (byte)0;
                byte b = prev[i];
                byte c = i >= 4 ? prev[i - 4] : (byte)0;
                byte predictor = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (byte)((a + b) / 2),
                    4 => Paeth(a, b, c),
                    _ => 0,
                };
                raw[o + 1 + i] = (byte)(line[i] - predictor);
            }

            (prev, line) = (line, prev);
        }

        using var output = new MemoryStream();
        output.Write(Signature);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8;  // ビット深度
        ihdr[9] = 6;  // RGBA
        WriteChunk(output, "IHDR", ihdr);

        using (var compressed = new MemoryStream())
        {
            using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(raw);
            }

            WriteChunk(output, "IDAT", compressed.ToArray());
        }

        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    public static PixelImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !data[..8].SequenceEqual(Signature))
        {
            throw new FormatException("PNG ではありません。");
        }

        int width = 0;
        int height = 0;
        int colorType = -1;
        using var idat = new MemoryStream();
        int pos = 8;
        while (pos + 8 <= data.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(data[pos..]);
            string type = System.Text.Encoding.ASCII.GetString(data.Slice(pos + 4, 4));
            if (length < 0 || pos + 12 + length > data.Length)
            {
                throw new FormatException("PNG が壊れています。");
            }

            ReadOnlySpan<byte> body = data.Slice(pos + 8, length);
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(body);
                    height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    if (body[8] != 8 || body[12] != 0)
                    {
                        throw new FormatException("8bit・インターレースなしの PNG だけに対応しています。");
                    }

                    colorType = body[9];
                    break;
                case "IDAT":
                    idat.Write(body);
                    break;
            }

            pos += 12 + length;
            if (type == "IEND")
            {
                break;
            }
        }

        int channels = colorType switch
        {
            6 => 4,
            2 => 3,
            4 => 2,
            0 => 1,
            _ => throw new FormatException("この色の形式の PNG には対応していません。"),
        };

        var image = new PixelImage(width, height);
        int stride = width * channels;
        var raw = new byte[height * (stride + 1)];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            z.ReadExactly(raw);
        }

        var prev = new byte[stride];
        var line = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            int o = y * (stride + 1);
            byte filter = raw[o];
            for (int i = 0; i < stride; i++)
            {
                byte a = i >= channels ? line[i - channels] : (byte)0;
                byte b = prev[i];
                byte c = i >= channels ? prev[i - channels] : (byte)0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new FormatException("PNG が壊れています（不明なフィルター）。"),
                };
                line[i] = (byte)(raw[o + 1 + i] + predictor);
            }

            for (int x = 0; x < width; x++)
            {
                int p = x * channels;
                uint argb = channels switch
                {
                    4 => (uint)line[p + 3] << 24 | (uint)line[p] << 16 | (uint)line[p + 1] << 8 | line[p + 2],
                    3 => 0xFF000000 | (uint)line[p] << 16 | (uint)line[p + 1] << 8 | line[p + 2],
                    2 => (uint)line[p + 1] << 24 | (uint)line[p] * 0x010101u,
                    _ => 0xFF000000 | (uint)line[p] * 0x010101u,
                };
                image.SetPixel(x, y, argb);
            }

            (prev, line) = (line, prev);
        }

        return image;
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> body)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
        System.Text.Encoding.ASCII.GetBytes(type, header[4..]);
        output.Write(header);
        output.Write(body);

        uint crc = 0xFFFFFFFF;
        crc = Crc(crc, header[4..]);
        crc = Crc(crc, body);
        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, crc ^ 0xFFFFFFFF);
        output.Write(tail);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
