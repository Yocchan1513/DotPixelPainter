using System.Numerics;
using DotPixelPainter.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.UI;

namespace DotPixelPainter;

/// <summary>
/// 色の段階パネル（AzPainter2 の「拡張パレット」にあたる）。
/// 上の段: 基準の色の影〜ハイライト（色相ずらしあり）。下: 基準の色相での鮮やかさ×明るさの表。右: 色相の帯。
/// 基準の色は勝手には変わらない（「今の描画色を基準にする」か、色相の帯で変える）。
/// </summary>
public sealed partial class MainWindow
{
    private const int ShadeColumns = 8;
    private const int ShadeRows = 6;
    private const float ShadeGap = 8;
    private const float HueStripWidth = 18;

    private uint _shadeBase = 0xFFD9774A;
    private bool _draggingHue;

    private void ShadeOption_Click(object sender, RoutedEventArgs e) => ShadeCanvas.Invalidate();

    private void ShadeBaseFromCurrent_Click(object sender, RoutedEventArgs e)
    {
        _shadeBase = _color | 0xFF000000;
        ShadeCanvas.Invalidate();
    }

    /// <summary>マス1つの大きさ（DIP）。パネルの幅に合わせる。</summary>
    private float ShadeCell => Math.Max(8, MathF.Floor(((float)ShadeCanvas.ActualWidth - ShadeGap - HueStripWidth) / ShadeColumns));

    private float ShadeGridTop => ShadeCell + ShadeGap;

    private void ShadeCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        CanvasDrawingSession ds = args.DrawingSession;
        float cell = ShadeCell;
        Color frame = Color.FromArgb(120, 128, 128, 128);

        // 上の段: 影〜ハイライト
        uint[] ramp = ShadeGenerator.Ramp(_shadeBase, ShadeColumns, 3, HueShiftToggle.IsChecked == true);
        for (int i = 0; i < ramp.Length; i++)
        {
            DrawShadeCell(ds, new Rect(i * cell, 0, cell, cell), ramp[i], frame, i == 3);
        }

        // 下: 鮮やかさ×明るさ
        int hue = ColorHsv.FromArgb(_shadeBase).H;
        uint[,] grid = ShadeGenerator.Grid(hue, ShadeColumns, ShadeRows);
        float top = ShadeGridTop;
        for (int r = 0; r < ShadeRows; r++)
        {
            for (int c = 0; c < ShadeColumns; c++)
            {
                DrawShadeCell(ds, new Rect(c * cell, top + r * cell, cell, cell), grid[r, c], frame, false);
            }
        }

        // 右: 色相の帯と、今の基準の位置
        float x = ShadeColumns * cell + ShadeGap;
        float height = ShadeRows * cell;
        var stops = Enumerable.Range(0, 7)
            .Select(i => new CanvasGradientStop { Position = i / 6f, Color = ToColor(new ColorHsv(i * 60 % 360, 255, 255).ToArgb()) })
            .ToArray();
        using (var gradient = new CanvasLinearGradientBrush(sender, stops) { StartPoint = new Vector2(0, top), EndPoint = new Vector2(0, top + height) })
        {
            ds.FillRectangle(x, top, HueStripWidth, height, gradient);
        }

        ds.DrawRectangle(x, top, HueStripWidth, height, frame, 1);
        float marker = top + height * hue / 360f;
        ds.DrawLine(x - 2, marker, x + HueStripWidth + 2, marker, Color.FromArgb(255, 0, 0, 0), 3);
        ds.DrawLine(x - 2, marker, x + HueStripWidth + 2, marker, Color.FromArgb(255, 255, 255, 255), 1);
    }

    private void DrawShadeCell(CanvasDrawingSession ds, Rect rect, uint argb, Color frame, bool isBase)
    {
        ds.FillRectangle(rect, ToColor(argb));
        bool selected = (argb | 0xFF000000) == (_color | 0xFF000000);
        if (selected || isBase)
        {
            var inner = new Rect(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
            ds.DrawRectangle(inner, selected ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(200, 0, 0, 0), selected ? 2 : 1);
        }
        else
        {
            ds.DrawRectangle(rect, frame, 0.5f);
        }
    }

    /// <summary>押した場所の色。色相の帯なら null（帯の操作は別）。</summary>
    private uint? ShadeColorAt(Point p)
    {
        float cell = ShadeCell;
        int c = (int)(p.X / cell);
        if (c < 0 || c >= ShadeColumns)
        {
            return null;
        }

        if (p.Y >= 0 && p.Y < cell)
        {
            return ShadeGenerator.Ramp(_shadeBase, ShadeColumns, 3, HueShiftToggle.IsChecked == true)[c];
        }

        int r = (int)((p.Y - ShadeGridTop) / cell);
        if (p.Y >= ShadeGridTop && r < ShadeRows)
        {
            return ShadeGenerator.Grid(ColorHsv.FromArgb(_shadeBase).H, ShadeColumns, ShadeRows)[r, c];
        }

        return null;
    }

    private bool IsOnHueStrip(Point p) => p.X >= ShadeColumns * ShadeCell + ShadeGap - 2 && p.Y >= ShadeGridTop;

    /// <summary>色相の帯の位置に合わせて基準の色相を変える。鮮やかさのない基準色は、見えるよう鮮やかにする。</summary>
    private void SetShadeHueFrom(Point p)
    {
        float height = ShadeRows * ShadeCell;
        int hue = (int)Math.Clamp((p.Y - ShadeGridTop) / height * 360, 0, 359);
        ColorHsv b = ColorHsv.FromArgb(_shadeBase);
        _shadeBase = new ColorHsv(hue, b.S < 40 ? 200 : b.S, b.V < 40 ? 200 : b.V).ToArgb();
        ShadeCanvas.Invalidate();
    }

    private void ShadeCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ShadeCanvas);
        if (IsOnHueStrip(point.Position))
        {
            _draggingHue = true;
            SetShadeHueFrom(point.Position);
            ShadeCanvas.CapturePointer(e.Pointer);
        }
        else if (ShadeColorAt(point.Position) is { } color)
        {
            if (point.Properties.IsRightButtonPressed)
            {
                // 右クリックはパレットに追加
                if (_palette.Add(color))
                {
                    OnPaletteChanged();
                }
            }
            else
            {
                SetCurrentColor(color);
                ShadeCanvas.Invalidate();
            }
        }

        e.Handled = true;
    }

    private void ShadeCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_draggingHue)
        {
            SetShadeHueFrom(e.GetCurrentPoint(ShadeCanvas).Position);
        }
    }

    private void ShadeCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _draggingHue = false;
        ShadeCanvas.ReleasePointerCapture(e.Pointer);
    }
}
