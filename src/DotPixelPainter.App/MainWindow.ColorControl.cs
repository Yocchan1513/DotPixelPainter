using System.Globalization;
using System.Numerics;
using DotPixelPainter.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace DotPixelPainter;

/// <summary>
/// コントロールパネル: 描画色と背景色、RGB/HSV スライダー、色コード、ブラシのサイズと形。
/// スライダーは起動を軽くするため、標準部品ではなく1枚の CanvasControl に描く。
/// </summary>
public sealed partial class MainWindow
{
    private const float BarRowHeight = 24;
    private const float BarLabelWidth = 18;
    private const float BarValueWidth = 34;

    private uint _backColor = 0xFFFFFFFF;
    private ColorHsv _hsv = new(0, 0, 0);
    private bool _hsvMode;
    private int _barDragRow = -1;
    private BrushMask _brush = BrushMask.Single;

    // ---- 描画色・背景色 ----

    /// <summary>描画色を変える。HSV スライダーから来たときは、丸め誤差を避けるため HSV をそのまま保つ。</summary>
    private void SetCurrentColor(uint argb, bool fromHsv = false)
    {
        _color = argb;
        if (!fromHsv)
        {
            ColorHsv hsv = ColorHsv.FromArgb(argb);
            // 無彩色（S=0）や黒（V=0）では色相が決まらないので、前の色相を残す
            _hsv = hsv.S == 0 || hsv.V == 0 ? hsv with { H = _hsv.H } : hsv;
        }

        ForeColorSwatch.Background = new SolidColorBrush(ToColor(argb));
        // 入力中の色コードは上書きしない（起動直後は XamlRoot がまだないので問い合わせない）
        if (Root.XamlRoot is null || !ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), ColorHexBox))
        {
            ColorHexBox.Text = $"#{argb:X8}";
        }

        UpdateSwatchSelection();
        ColorBarsCanvas.Invalidate();

        if (_colorPicker is not null && !_syncingPicker)
        {
            _syncingPicker = true;
            _colorPicker.Color = ToColor(argb);
            _syncingPicker = false;
        }
    }

    private void SetBackColor(uint argb)
    {
        _backColor = argb;
        BackColorSwatch.Background = new SolidColorBrush(ToColor(argb));
        PreviewCanvas.Invalidate(); // プレビューの背景を「背景色」にしているとき用
    }

    private void SwapColors()
    {
        uint fore = _color;
        SetCurrentColor(_backColor);
        SetBackColor(fore);
    }

    private void ResetColors()
    {
        SetCurrentColor(0xFF000000);
        SetBackColor(0xFFFFFFFF);
    }

    private void BackColor_Tapped(object sender, TappedRoutedEventArgs e) => SwapColors();

    // ---- 色コード ----

    private void ColorHexBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ApplyHexBox();
            e.Handled = true;
        }
    }

    private void ColorHexBox_LostFocus(object sender, RoutedEventArgs e) => ApplyHexBox();

    private void ApplyHexBox()
    {
        string text = ColorHexBox.Text.Trim().TrimStart('#');
        if (uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value)
            && (text.Length == 6 || text.Length == 8))
        {
            SetCurrentColor(text.Length == 6 ? 0xFF000000 | value : value);
        }

        ColorHexBox.Text = $"#{_color:X8}"; // 読めなかったときは元に戻す
    }

    // ---- RGB / HSV スライダー ----

    private void ColorMode_Click(object sender, RoutedEventArgs e)
    {
        _hsvMode = HsvToggle.IsChecked == true;
        ColorBarsCanvas.Invalidate();
    }

    private (string Label, int Value, int Max)[] BarRows()
    {
        int a = (int)(_color >> 24);
        if (_hsvMode)
        {
            return [("H", _hsv.H, 359), ("S", _hsv.S, 255), ("V", _hsv.V, 255), ("A", a, 255)];
        }

        return [("R", (int)((_color >> 16) & 0xFF), 255), ("G", (int)((_color >> 8) & 0xFF), 255), ("B", (int)(_color & 0xFF), 255), ("A", a, 255)];
    }

    /// <summary>row 番目のスライダーの値を変えたときの色。</summary>
    private void SetBarValue(int row, int value)
    {
        byte alpha = (byte)(_color >> 24);
        if (row == 3)
        {
            SetCurrentColor((_color & 0x00FFFFFF) | (uint)Math.Clamp(value, 0, 255) << 24, fromHsv: _hsvMode);
            return;
        }

        if (_hsvMode)
        {
            _hsv = row switch
            {
                0 => _hsv with { H = Math.Clamp(value, 0, 359) },
                1 => _hsv with { S = Math.Clamp(value, 0, 255) },
                _ => _hsv with { V = Math.Clamp(value, 0, 255) },
            };
            SetCurrentColor(_hsv.ToArgb(alpha), fromHsv: true);
            return;
        }

        int shift = 16 - row * 8;
        uint v = (uint)Math.Clamp(value, 0, 255);
        SetCurrentColor((_color & ~(0xFFu << shift)) | v << shift);
    }

    /// <summary>row 番目のスライダーの左端（0）と右端（最大）の色。グラデーション用。</summary>
    private Color[] BarGradient(int row)
    {
        uint opaque = _color | 0xFF000000;
        if (row == 3)
        {
            return [ToColor(opaque & 0x00FFFFFF), ToColor(opaque)];
        }

        if (_hsvMode)
        {
            return row switch
            {
                // 色相は見やすさのため、彩度と明度を最大にして7色で描く
                0 => [.. Enumerable.Range(0, 7).Select(i => ToColor(new ColorHsv(i * 60 % 360, 255, 255).ToArgb()))],
                1 => [ToColor((_hsv with { S = 0 }).ToArgb()), ToColor((_hsv with { S = 255 }).ToArgb())],
                _ => [ToColor((_hsv with { V = 0 }).ToArgb()), ToColor((_hsv with { V = 255 }).ToArgb())],
            };
        }

        int shift = 16 - row * 8;
        uint low = opaque & ~(0xFFu << shift);
        uint high = low | 0xFFu << shift;
        return [ToColor(low), ToColor(high)];
    }

    private void ColorBars_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        CanvasDrawingSession ds = args.DrawingSession;
        var rows = BarRows();
        float width = (float)sender.ActualWidth;
        float barX = BarLabelWidth;
        float barW = Math.Max(10, width - BarLabelWidth - BarValueWidth);
        Color text = ActualThemeIsDark() ? Color.FromArgb(255, 230, 230, 230) : Color.FromArgb(255, 30, 30, 30);
        Color frame = Color.FromArgb(160, 128, 128, 128);
        using var font = new CanvasTextFormat { FontSize = 12, VerticalAlignment = CanvasVerticalAlignment.Center };
        using var valueFont = new CanvasTextFormat
        {
            FontSize = 12,
            VerticalAlignment = CanvasVerticalAlignment.Center,
            HorizontalAlignment = CanvasHorizontalAlignment.Right,
        };

        for (int row = 0; row < rows.Length; row++)
        {
            (string label, int value, int max) = rows[row];
            float y = row * BarRowHeight;
            float barY = y + 6;
            float barH = BarRowHeight - 12;

            ds.DrawText(label, 0, y, BarLabelWidth, BarRowHeight, text, font);

            var barRect = new Windows.Foundation.Rect(barX, barY, barW, barH);
            if (row == 3)
            {
                CanvasImageBrush checker = GetChecker(sender);
                checker.Transform = Matrix3x2.CreateScale(0.5f) * Matrix3x2.CreateTranslation(barX, barY);
                ds.FillRectangle(barRect, checker);
            }

            Color[] colors = BarGradient(row);
            var stops = colors.Select((c, i) => new CanvasGradientStop { Color = c, Position = i / (float)(colors.Length - 1) }).ToArray();
            using (var gradient = new CanvasLinearGradientBrush(sender, stops)
            {
                StartPoint = new Vector2(barX, 0),
                EndPoint = new Vector2(barX + barW, 0),
            })
            {
                ds.FillRectangle(barRect, gradient);
            }

            ds.DrawRectangle(barRect, frame, 1);

            // つまみ（白と黒の二重線でどの色の上でも見える）
            float mx = barX + barW * value / max;
            ds.DrawLine(mx, y + 3, mx, y + BarRowHeight - 3, Color.FromArgb(255, 0, 0, 0), 3);
            ds.DrawLine(mx, y + 3, mx, y + BarRowHeight - 3, Color.FromArgb(255, 255, 255, 255), 1);

            ds.DrawText(value.ToString(CultureInfo.InvariantCulture), barX + barW, y, BarValueWidth, BarRowHeight, text, valueFont);
        }
    }

    private bool ActualThemeIsDark() => Root.ActualTheme == ElementTheme.Dark;

    private (int Row, int Value) BarHit(Windows.Foundation.Point p)
    {
        int row = (int)(p.Y / BarRowHeight);
        if (row < 0 || row > 3)
        {
            return (-1, 0);
        }

        float barW = Math.Max(10, (float)ColorBarsCanvas.ActualWidth - BarLabelWidth - BarValueWidth);
        int max = BarRows()[row].Max;
        double t = Math.Clamp((p.X - BarLabelWidth) / barW, 0, 1);
        return (row, (int)Math.Round(t * max));
    }

    private void ColorBars_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        (int row, int value) = BarHit(e.GetCurrentPoint(ColorBarsCanvas).Position);
        if (row < 0)
        {
            return;
        }

        _barDragRow = row;
        SetBarValue(row, value);
        ColorBarsCanvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ColorBars_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_barDragRow < 0)
        {
            return;
        }

        var p = e.GetCurrentPoint(ColorBarsCanvas).Position;
        (_, int value) = BarHit(new Windows.Foundation.Point(p.X, _barDragRow * BarRowHeight + 1));
        SetBarValue(_barDragRow, value);
    }

    private void ColorBars_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _barDragRow = -1;
        ColorBarsCanvas.ReleasePointerCapture(e.Pointer);
    }

    private void ColorBars_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => _barDragRow = -1;

    /// <summary>ホイールで1ずつ（Shift で10ずつ）値を動かす。</summary>
    private void ColorBars_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ColorBarsCanvas);
        int row = (int)(point.Position.Y / BarRowHeight);
        if (row < 0 || row > 3)
        {
            return;
        }

        int step = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) ? 10 : 1;
        int delta = point.Properties.MouseWheelDelta > 0 ? step : -step;
        SetBarValue(row, BarRows()[row].Value + delta);
        e.Handled = true;
    }

    // ---- ブラシ ----

    private void BrushSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e) => UpdateBrush();

    private void BrushShape_Click(object sender, RoutedEventArgs e)
    {
        bool round = ReferenceEquals(sender, RoundBrushToggle);
        SquareBrushToggle.IsChecked = !round;
        RoundBrushToggle.IsChecked = round;
        UpdateBrush();
    }

    private void ChangeBrushSize(int delta) =>
        BrushSizeSlider.Value = Math.Clamp(BrushSizeSlider.Value + delta, 1, BrushMask.MaxSize);

    private void UpdateBrush()
    {
        // XAML 読み込み中に呼ばれたときは、まだ部品がそろっていない
        if (BrushSizeText is null || RoundBrushToggle is null)
        {
            return;
        }

        _brush = BrushMask.Create((int)BrushSizeSlider.Value, RoundBrushToggle.IsChecked == true);
        BrushSizeText.Text = $"ブラシ {_brush.Size}px";
        UpdateStatus(null);
    }
}
