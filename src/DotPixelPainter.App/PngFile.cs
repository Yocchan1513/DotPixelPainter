using DotPixelPainter.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace DotPixelPainter;

/// <summary>PNGの読み書き。OS標準のコーデックを使い、外部ライブラリは使わない。</summary>
public static class PngFile
{
    public static async Task<PixelDocument> LoadAsync(StorageFile file)
    {
        using IRandomAccessStream stream = await file.OpenReadAsync();
        PixelImage image = await DecodeAsync(stream);
        var document = new PixelDocument(file.Name, image.Width, image.Height)
        {
            FilePath = file.Path,
        };
        document.ActiveLayer.Image.LoadFromBgra(BgraOf(image));
        return document;
    }

    /// <summary>OS が読める画像（PNG・BMP・JPEG など。クリップボードの画像も）を1枚の画像にする。</summary>
    public static async Task<PixelImage> DecodeAsync(IRandomAccessStream stream)
    {
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        if (decoder.PixelWidth > PixelImage.MaxSize || decoder.PixelHeight > PixelImage.MaxSize)
        {
            throw new InvalidOperationException($"画像が大きすぎます（{decoder.PixelWidth}×{decoder.PixelHeight}）。{PixelImage.MaxSize}×{PixelImage.MaxSize} までに対応しています。");
        }

        PixelDataProvider data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var image = new PixelImage((int)decoder.PixelWidth, (int)decoder.PixelHeight);
        image.LoadFromBgra(data.DetachPixelData());
        return image;
    }

    private static byte[] BgraOf(PixelImage image)
    {
        var bytes = new byte[image.Width * image.Height * 4];
        image.CopyToBgra(bytes);
        return bytes;
    }

    /// <summary>表示中のレイヤーを統合して1枚のPNGとして保存する。</summary>
    public static async Task SaveAsync(PixelDocument document, StorageFile file)
    {
        PixelImage image = document.Composite();
        var bytes = new byte[image.Width * image.Height * 4];
        image.CopyToBgra(bytes);

        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        stream.Size = 0;
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            (uint)image.Width,
            (uint)image.Height,
            96,
            96,
            bytes);
        await encoder.FlushAsync();
    }
}
