using DotPixelPainter.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DotPixelPainter;

/// <summary>カラーマスクの設定。描く道具（鉛筆・消しゴム・図形・塗りつぶし）のひと筆にだけ使う。</summary>
public sealed partial class MainWindow
{
    private readonly ColorMask _mask = new();

    private void MaskMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _mask.Mode = (ColorMaskMode)Math.Max(0, MaskModeBox.SelectedIndex);
        UpdateStatus(null);
    }

    private void MaskAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_mask.Add(_color))
        {
            // 色を足したらすぐ効くように、オフなら「指定色を保護」にする
            if (_mask.Mode == ColorMaskMode.Off)
            {
                MaskModeBox.SelectedIndex = (int)ColorMaskMode.Protect;
            }

            RebuildMaskSwatches();
        }
    }

    private void MaskClear_Click(object sender, RoutedEventArgs e)
    {
        _mask.Clear();
        RebuildMaskSwatches();
    }

    private void RebuildMaskSwatches()
    {
        MaskSwatches.Children.Clear();
        var stroke = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"];
        foreach (uint color in _mask.Colors)
        {
            uint c = color;
            var swatch = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = stroke,
                Background = new SolidColorBrush(ToColor(c >> 24 == 0 ? 0x00000000 : c)),
            };
            ToolTipService.SetToolTip(swatch, c >> 24 == 0 ? "透明（クリックで外す）" : $"#{c:X8}（クリックで外す）");
            swatch.Tapped += (_, _) =>
            {
                _mask.Remove(c);
                RebuildMaskSwatches();
            };
            MaskSwatches.Children.Add(swatch);
        }

        UpdateStatus(null);
    }

    private string MaskStatus() => _mask.IsActive
        ? $"　｜　マスク: {(_mask.Mode == ColorMaskMode.Protect ? "保護" : "限定")} {_mask.Colors.Count}色"
        : "";
}
