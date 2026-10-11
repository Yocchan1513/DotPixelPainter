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

    /// <summary>
    /// OS が読める画像（PNG・BMP・JPEG など。クリップボードの画像も）を1枚の画像にする。
    /// forReference なら（分割画面の資料用）、大きすぎる画像は縮めて読み、縮めないときは写真の向き（EXIF）にも合わせる。
    /// </summary>
    public static async Task<PixelImage> DecodeAsync(IRandomAccessStream stream, bool forReference = false)
    {
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        bool tooLarge = decoder.PixelWidth > PixelImage.MaxSize || decoder.PixelHeight > PixelImage.MaxSize;

        // 写真の向きに合わせるのは、縮めないときだけ（縮めるときに向きも変えると、幅と高さの扱いがあいまいになるため）
        bool orient = forReference && !tooLarge;
        uint width = orient ? decoder.OrientedPixelWidth : decoder.PixelWidth;
        uint height = orient ? decoder.OrientedPixelHeight : decoder.PixelHeight;
        var transform = new BitmapTransform();
        if (tooLarge)
        {
            if (!forReference)
            {
                throw new InvalidOperationException(T($"画像が大きすぎます（{decoder.PixelWidth}×{decoder.PixelHeight}）。{PixelImage.MaxSize}×{PixelImage.MaxSize} までに対応しています。", $"The image is too large ({decoder.PixelWidth}×{decoder.PixelHeight}). Up to {PixelImage.MaxSize}×{PixelImage.MaxSize} is supported."));
            }

            double scale = (double)PixelImage.MaxSize / Math.Max(width, height);
            width = Math.Max(1, (uint)(width * scale));
            height = Math.Max(1, (uint)(height * scale));
            transform.ScaledWidth = width;
            transform.ScaledHeight = height;
            transform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        PixelDataProvider data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            transform,
            orient ? ExifOrientationMode.RespectExifOrientation : ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var image = new PixelImage((int)width, (int)height);
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
    /// <summary>表示どおりに1枚にまとめて PNG で保存する。scale が 2 以上なら、ドットのまま整数倍に拡大して書き出す。</summary>
    public static async Task SaveAsync(PixelDocument document, StorageFile file, int scale = 1)
    {
        PixelImage image = document.Composite();
        if (scale > 1)
        {
            image = ImageTransform.ScaleNearest(image, image.Width * scale, image.Height * scale);
        }

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
