using DotPixelPainter.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace DotPixelPainter;

/// <summary>
/// コピー・切り取り・貼り付けと、選択範囲の反転・回転。選択ツールでの右クリックメニューもここ。
/// クリップボードには「PNG」形式（透明度を保てる。GIMP や Krita などと同じ）と、普通の画像の両方を置く。
/// </summary>
public sealed partial class MainWindow
{
    private const string PngClipboardFormat = "PNG";

    private PixelImage? _lastCopied;
    private MenuFlyout? _selectionMenu;

    /// <summary>選択範囲（持ち上げ中ならその中身、範囲がなければレイヤー全体）を画像として取り出す。</summary>
    private static PixelImage? SelectedImage(DocumentTab tab)
    {
        if (tab.Floating is { } floating)
        {
            return floating.ToImage();
        }

        PixelImage layer = tab.Document.ActiveLayer.Image;
        PixelRect rect = tab.Selection ?? new PixelRect(0, 0, layer.Width, layer.Height);
        var image = new PixelImage(rect.Width, rect.Height);
        for (int y = 0; y < rect.Height; y++)
        {
            for (int x = 0; x < rect.Width; x++)
            {
                image.SetPixel(x, y, layer.GetPixel(rect.X + x, rect.Y + y));
            }
        }

        return image;
    }

    private async Task CopyAsync(bool cut)
    {
        if (CurrentTab is not { } tab || SelectedImage(tab) is not { } image)
        {
            return;
        }

        _lastCopied = image;
        try
        {
            byte[] png = PngCodec.Encode(image);
            var package = new DataPackage();
            package.SetData(PngClipboardFormat, await ToStreamAsync(png));
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(await ToStreamAsync(png)));
            Clipboard.SetContent(package);
            Clipboard.Flush(); // アプリを閉じたあとも貼り付けられるように
        }
        catch (Exception ex)
        {
            // クリップボードが他のアプリに使われていても、アプリ内での貼り付けはできる
            ErrorLog.Write("クリップボードへのコピー", ex);
        }

        if (cut)
        {
            DeleteSelection();
        }
    }

    private static async Task<IRandomAccessStream> ToStreamAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        return stream;
    }

    /// <summary>クリップボードの画像を読む。PNG 形式を優先し、なければ普通の画像、どちらもなければ最後にコピーした画像。</summary>
    private async Task<PixelImage?> ReadClipboardImageAsync()
    {
        try
        {
            DataPackageView content = Clipboard.GetContent();
            if (content.Contains(PngClipboardFormat) && await content.GetDataAsync(PngClipboardFormat) is IRandomAccessStream png)
            {
                return await PngFile.DecodeAsync(png);
            }

            if (content.Contains(StandardDataFormats.Bitmap))
            {
                RandomAccessStreamReference reference = await content.GetBitmapAsync();
                using IRandomAccessStreamWithContentType stream = await reference.OpenReadAsync();
                return await PngFile.DecodeAsync(stream);
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("クリップボードからの貼り付け", ex);
        }

        return _lastCopied;
    }

    /// <summary>貼り付けた画像を、選択範囲の位置（なければ画像の中央）に浮かせる。動かしてから確定できる。</summary>
    private async Task PasteAsync()
    {
        if (CurrentTab is not { } tab)
        {
            return;
        }

        PixelImage? image;
        try
        {
            image = await ReadClipboardImageAsync();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("貼り付けられませんでした", ex.Message);
            return;
        }

        if (image is null)
        {
            return;
        }

        CommitFloating(tab);
        SelectTool(Tool.Select);
        int x = tab.Selection?.X ?? (tab.Document.Width - image.Width) / 2;
        int y = tab.Selection?.Y ?? (tab.Document.Height - image.Height) / 2;
        tab.FloatStroke = tab.Document.BeginStroke();
        tab.Floating = FloatingSelection.FromImage(image, x, y);
        tab.Selection = tab.Floating.Bounds;
        tab.ImageChanged = true;
        Canvas.Invalidate();
        PreviewCanvas.Invalidate();
    }

    // ---- 反転・回転 ----

    /// <summary>選択範囲の中身を変形する。範囲がなければレイヤー全体を対象にする。</summary>
    private void TransformSelection(Action<FloatingSelection> transform)
    {
        if (CurrentTab is not { } tab)
        {
            return;
        }

        tab.Selection ??= ImageRect(tab);
        SelectTool(Tool.Select);
        EnsureLifted(tab);
        if (tab.Floating is { } floating)
        {
            transform(floating);
            tab.Selection = floating.Bounds;
            tab.ImageChanged = true;
            Canvas.Invalidate();
            PreviewCanvas.Invalidate();
        }
    }

    private void FlipSelectionHorizontal() => TransformSelection(f => f.FlipHorizontal());

    private void FlipSelectionVertical() => TransformSelection(f => f.FlipVertical());

    private void RotateSelection(bool clockwise) => TransformSelection(f => f.Rotate90(clockwise));

    // ---- 選択ツールの右クリックメニュー（初めて使うときに作る） ----

    private void ShowSelectionMenu(Point position)
    {
        if (_selectionMenu is null)
        {
            _selectionMenu = new MenuFlyout();
            AddMenuItem("切り取り", "Ctrl+X", () => ErrorLog.Run("切り取り", () => CopyAsync(cut: true)));
            AddMenuItem("コピー", "Ctrl+C", () => ErrorLog.Run("コピー", () => CopyAsync(cut: false)));
            AddMenuItem("貼り付け", "Ctrl+V", () => ErrorLog.Run("貼り付け", PasteAsync));
            AddMenuItem("消去", "Delete", () => DeleteSelection());
            _selectionMenu.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem("左右反転", "Shift+H", FlipSelectionHorizontal);
            AddMenuItem("上下反転", "Shift+V", FlipSelectionVertical);
            AddMenuItem("右に90°回転", "Shift+R", () => RotateSelection(clockwise: true));
            AddMenuItem("左に90°回転", "Shift+L", () => RotateSelection(clockwise: false));
            _selectionMenu.Items.Add(new MenuFlyoutSeparator());
            AddMenuItem("すべて選択", "Ctrl+A", SelectAll);
            AddMenuItem("選択を解除", "Ctrl+D", Deselect);
        }

        _selectionMenu.ShowAt(Canvas, new FlyoutShowOptions { Position = position });
    }

    private void AddMenuItem(string text, string keys, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, KeyboardAcceleratorTextOverride = keys };
        item.Click += (_, _) => action();
        _selectionMenu!.Items.Add(item);
    }
}
