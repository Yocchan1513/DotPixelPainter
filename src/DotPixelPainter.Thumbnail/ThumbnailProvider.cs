using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using DotPixelPainter.Core;

namespace DotPixelPainter.Thumbnail;

// エクスプローラーのサムネイルの仕組み（COM）に必要なインターフェース。番号は Windows で決まっている

[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    void CreateInstance(nint outer, in Guid riid, out nint ppv);

    void LockServer([MarshalAs(UnmanagedType.Bool)] bool doLock);
}

[GeneratedComInterface]
[Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
internal partial interface IInitializeWithStream
{
    /// <summary>stream は IStream。ここでは中身を読むだけなので、ポインターのまま受け取る。</summary>
    void Initialize(nint stream, uint mode);
}

[GeneratedComInterface]
[Guid("e357fccd-a995-4576-b01f-234630154e96")]
internal partial interface IThumbnailProvider
{
    void GetThumbnail(uint size, out nint bitmap, out int alphaType);
}

/// <summary>
/// .dotpix のサムネイルを作る。.dotpix（zip）の中の merged.png（表示どおりに1枚にした絵）を読み、
/// ドットがにじまないよう、色を混ぜずに枠の大きさへ合わせる。
/// </summary>
[GeneratedComClass]
internal sealed partial class ThumbnailProvider : IInitializeWithStream, IThumbnailProvider
{
    /// <summary>これより大きいファイルは読まない（エクスプローラーを重くしないため）。</summary>
    private const int MaxFileBytes = 64 * 1024 * 1024;

    private const int WtsAlphaTypeArgb = 2;

    private byte[]? _file;

    public void Initialize(nint stream, uint mode)
    {
        _file = StreamReader.ReadAll(stream, MaxFileBytes);
    }

    public void GetThumbnail(uint size, out nint bitmap, out int alphaType)
    {
        if (_file is null)
        {
            throw new InvalidOperationException("まだ読み込んでいません。");
        }

        PixelImage image = ReadMerged(_file);
        PixelImage fitted = Fit(image, (int)Math.Max(1, size));
        bitmap = DibSection.Create(fitted);
        alphaType = WtsAlphaTypeArgb;
    }

    /// <summary>.dotpix の中の merged.png を読む。</summary>
    internal static PixelImage ReadMerged(byte[] file)
    {
        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        ZipArchiveEntry entry = zip.GetEntry("merged.png") ?? throw new InvalidDataException("merged.png がありません。");
        using Stream png = entry.Open();
        using var buffer = new MemoryStream();
        png.CopyTo(buffer);
        return PngCodec.Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    /// <summary>
    /// size × size の枠に収まるようにする。小さい絵は整数倍に拡大（くっきり見せる）、大きい絵は色を混ぜずに縮小する。
    /// </summary>
    internal static PixelImage Fit(PixelImage image, int size)
    {
        int longest = Math.Max(image.Width, image.Height);
        if (longest <= size)
        {
            int times = size / longest;
            return times <= 1 ? image : ImageTransform.ScaleNearest(image, image.Width * times, image.Height * times);
        }

        int width = Math.Max(1, (int)((long)image.Width * size / longest));
        int height = Math.Max(1, (int)((long)image.Height * size / longest));
        return ImageTransform.ScaleNearest(image, width, height);
    }
}

[GeneratedComClass]
internal sealed partial class ClassFactory : IClassFactory
{
    private const int ClassNoAggregation = unchecked((int)0x80040110);

    public void CreateInstance(nint outer, in Guid riid, out nint ppv)
    {
        ppv = 0;
        if (outer != 0)
        {
            throw new COMException("集約はできません。", ClassNoAggregation);
        }

        nint unknown = Exports.ComWrappers.GetOrCreateComInterfaceForObject(new ThumbnailProvider(), CreateComInterfaceFlags.None);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in riid, out ppv));
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    public void LockServer(bool doLock)
    {
    }
}

/// <summary>IStream の中身を読む（ISequentialStream::Read を直接呼ぶ）。</summary>
internal static unsafe class StreamReader
{
    public static byte[] ReadAll(nint stream, int maxBytes)
    {
        if (stream == 0)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        // vtable の 4番目（0 から数えて 3）が Read（QueryInterface・AddRef・Release の次）
        nint* vtable = *(nint**)stream;
        var read = (delegate* unmanaged[Stdcall]<nint, byte*, uint, uint*, int>)vtable[3];

        using var output = new MemoryStream();
        byte[] chunk = new byte[64 * 1024];
        while (true)
        {
            uint got;
            int hr;
            fixed (byte* p = chunk)
            {
                hr = read(stream, p, (uint)chunk.Length, &got);
            }

            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            output.Write(chunk, 0, (int)got);
            if (output.Length > maxBytes)
            {
                throw new InvalidDataException("ファイルが大きすぎます。");
            }

            // S_FALSE（1）や読めた量が0なら終わり
            if (got == 0 || hr == 1)
            {
                break;
            }
        }

        return output.ToArray();
    }
}

/// <summary>絵を Windows のビットマップ（上から下へ並ぶ 32bit、乗算済みアルファ）にする。</summary>
internal static unsafe partial class DibSection
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint hdc, BitmapInfoHeader* info, uint usage, void** bits, nint section, uint offset);

    public static nint Create(PixelImage image)
    {
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = image.Width,
            Height = -image.Height, // 負の値で、上の行から順に並ぶ
            Planes = 1,
            BitCount = 32,
        };

        void* bits;
        nint bitmap = CreateDIBSection(0, &header, 0, &bits, 0, 0);
        if (bitmap == 0 || bits is null)
        {
            throw new OutOfMemoryException("ビットマップを作れませんでした。");
        }

        image.CopyToBgraPremultiplied(new Span<byte>(bits, image.Width * image.Height * 4));
        return bitmap;
    }
}
