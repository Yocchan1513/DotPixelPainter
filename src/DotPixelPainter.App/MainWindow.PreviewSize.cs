using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace DotPixelPainter;

/// <summary>
/// プレビューの高さ。下端のつまみをドラッグして変え、ダブルクリックで元に戻す。
/// 通常・タイル表示・スキンの 3D でそれぞれ覚えて、view.txt に保存する。
/// </summary>
public sealed partial class MainWindow
{
    private const double MinPreviewHeight = 60;
    private const double MaxPreviewHeight = 1200;

    private double _gripStartY;
    private double _gripStartHeight;
    private bool _gripDragging;

    private string PreviewSizeKey() =>
        IsSkinTab(CurrentTab) ? "preview-skin" : TileToggle.IsChecked == true ? "preview-tile" : "preview-normal";

    private double DefaultPreviewHeight() =>
        IsSkinTab(CurrentTab) ? SkinPreviewHeight : TileToggle.IsChecked == true ? TilePreviewHeight : NormalPreviewHeight;

    /// <summary>今の表示のしかたに合わせて、プレビューの高さを決め直す。</summary>
    private void ApplyPreviewHeight()
    {
        PreviewCanvas.Height = Math.Clamp(ViewSettings.GetDouble(PreviewSizeKey()) ?? DefaultPreviewHeight(), MinPreviewHeight, MaxPreviewHeight);
    }

    private void PreviewGrip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var grip = (UIElement)sender;
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _gripDragging = grip.CapturePointer(e.Pointer);
        _gripStartY = e.GetCurrentPoint(null).Position.Y;
        _gripStartHeight = PreviewCanvas.ActualHeight;
        e.Handled = true;
    }

    private void PreviewGrip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_gripDragging)
        {
            return;
        }

        double dy = e.GetCurrentPoint(null).Position.Y - _gripStartY;
        PreviewCanvas.Height = Math.Clamp(_gripStartHeight + dy, MinPreviewHeight, MaxPreviewHeight);
        e.Handled = true;
    }

    private void PreviewGrip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_gripDragging)
        {
            return;
        }

        _gripDragging = false;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        ViewSettings.SetDouble(PreviewSizeKey(), PreviewCanvas.Height);
        e.Handled = true;
    }

    private void PreviewGrip_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => _gripDragging = false;

    private void PreviewGrip_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        ViewSettings.Set(PreviewSizeKey(), "");
        ApplyPreviewHeight();
        e.Handled = true;
    }
}
