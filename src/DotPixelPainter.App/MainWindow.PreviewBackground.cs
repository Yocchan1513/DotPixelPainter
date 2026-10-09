using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace DotPixelPainter;

/// <summary>プレビューの背景。透明部分を、実際に置く場所の色で確かめるため。</summary>
public sealed partial class MainWindow
{
    private enum PreviewBackground
    {
        Checker,
        White,
        Black,
        Gray,
        BackColor,
    }

    private PreviewBackground _previewBackground = PreviewBackground.Checker;
    private MenuFlyout? _previewBackgroundMenu;

    /// <summary>塗る色。市松模様のときは null。</summary>
    private Color? PreviewBackgroundColor() => _previewBackground switch
    {
        PreviewBackground.White => Color.FromArgb(255, 255, 255, 255),
        PreviewBackground.Black => Color.FromArgb(255, 0, 0, 0),
        PreviewBackground.Gray => Color.FromArgb(255, 128, 128, 128),
        PreviewBackground.BackColor => ToColor(_backColor | 0xFF000000),
        _ => null,
    };

    private const double TilePreviewHeight = 220;

    private void TileToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!IsSkinTab(CurrentTab))
        {
            PreviewCanvas.Height = TileToggle.IsChecked == true ? TilePreviewHeight : NormalPreviewHeight;
        }

        PreviewCanvas.Invalidate();
    }

    /// <summary>
    /// 絵を 3×3 に並べて描く。ドットがにじまないよう、物理ピクセルの整数倍（入りきらないときは縮小）で描く。
    /// </summary>
    private void DrawTiledPreview(CanvasControl sender, CanvasDrawingSession ds, CanvasBitmap bitmap, Core.PixelDocument document)
    {
        float scale = sender.Dpi / 96f;
        float availW = (float)sender.ActualWidth * scale;
        float availH = (float)sender.ActualHeight * scale;
        float fit = Math.Min(availW / (document.Width * 3), availH / (document.Height * 3));
        float times = fit >= 1 ? MathF.Floor(Math.Min(fit, 4)) : fit;
        float w = document.Width * times / scale;
        float h = document.Height * times / scale;

        if (PreviewBackgroundColor() is { } background)
        {
            ds.FillRectangle(0, 0, w * 3, h * 3, background);
        }
        else
        {
            CanvasImageBrush checker = GetChecker(sender);
            checker.Transform = System.Numerics.Matrix3x2.Identity;
            ds.FillRectangle(0, 0, w * 3, h * 3, checker);
        }

        var source = new Windows.Foundation.Rect(0, 0, document.Width, document.Height);
        for (int ty = 0; ty < 3; ty++)
        {
            for (int tx = 0; tx < 3; tx++)
            {
                ds.DrawImage(bitmap, new Windows.Foundation.Rect(tx * w, ty * h, w, h), source, 1f, CanvasImageInterpolation.NearestNeighbor);
            }
        }
    }

    private void PreviewBackground_Click(object sender, RoutedEventArgs e)
    {
        if (_previewBackgroundMenu is null)
        {
            _previewBackgroundMenu = new MenuFlyout();
            AddBackgroundItem("市松模様", PreviewBackground.Checker);
            AddBackgroundItem("白", PreviewBackground.White);
            AddBackgroundItem("黒", PreviewBackground.Black);
            AddBackgroundItem("灰色", PreviewBackground.Gray);
            AddBackgroundItem("背景色", PreviewBackground.BackColor);
        }

        // Tag に C# の列挙型を入れると Native AOT で変換に失敗するおそれがあるので、対応表で持つ
        foreach (var (radio, value) in _previewBackgroundItems)
        {
            radio.IsChecked = value == _previewBackground;
        }

        _previewBackgroundMenu.ShowAt((FrameworkElement)sender);
    }

    private readonly List<(RadioMenuFlyoutItem Item, PreviewBackground Value)> _previewBackgroundItems = [];

    private void AddBackgroundItem(string text, PreviewBackground value)
    {
        var item = new RadioMenuFlyoutItem { Text = text, GroupName = "preview-bg" };
        _previewBackgroundItems.Add((item, value));
        item.Click += (_, _) =>
        {
            _previewBackground = value;
            PreviewCanvas.Invalidate();
        };
        _previewBackgroundMenu!.Items.Add(item);
    }
}
