using DotPixelPainter.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace DotPixelPainter;

public enum Tool
{
    Pencil,
    Eraser,
    Fill,
    Picker,
    Select,
}

public enum ShapeKind
{
    Freehand,
    Line,
    Rectangle,
    FilledRectangle,
    Ellipse,
    FilledEllipse,
}

/// <summary>ツールパネル（道具と描き方）と、図形・塗りつぶしの入力。</summary>
public sealed partial class MainWindow
{
    private static readonly Color EraserPreviewColor = Color.FromArgb(150, 255, 255, 255);

    private readonly Dictionary<Tool, ToggleButton> _toolButtons = [];
    private readonly Dictionary<ShapeKind, ToggleButton> _shapeButtons = [];
    private Tool _tool = Tool.Pencil;
    private ShapeKind _shape = ShapeKind.Freehand;
    private (int X, int Y) _shapeStart;
    private (int X, int Y) _shapeEnd;

    private static string ToolName(Tool tool) => tool switch
    {
        Tool.Pencil => "鉛筆",
        Tool.Eraser => "消しゴム",
        Tool.Fill => "塗りつぶし",
        Tool.Select => "選択",
        _ => "スポイト",
    };

    private static string ShapeName(ShapeKind shape) => shape switch
    {
        ShapeKind.Freehand => "自由線",
        ShapeKind.Line => "直線",
        ShapeKind.Rectangle => "四角",
        ShapeKind.FilledRectangle => "塗り四角",
        ShapeKind.Ellipse => "円",
        _ => "塗り円",
    };

    private string ToolStatus() =>
        _tool is Tool.Pencil or Tool.Eraser ? $"{ToolName(_tool)}・{ShapeName(_shape)}・{_brush.Size}px" : ToolName(_tool);

    private void BuildToolPanel()
    {
        AddToolButton(Tool.Pencil, Glyph(""), "B");
        AddToolButton(Tool.Eraser, Glyph(""), "E");
        AddToolButton(Tool.Fill, DropIcon(), "G");
        AddToolButton(Tool.Select, new Rectangle { Width = 15, Height = 13, Stroke = IconBrush, StrokeThickness = 1.4, StrokeDashArray = { 2, 1.5 } }, "M");
        AddToolButton(Tool.Picker, Glyph(""), "I");

        AddShapeButton(ShapeKind.Freehand, FreehandIcon(), "1");
        AddShapeButton(ShapeKind.Line, new Line { X1 = 3, Y1 = 15, X2 = 15, Y2 = 3, Stroke = IconBrush, StrokeThickness = 1.6 }, "2");
        AddShapeButton(ShapeKind.Rectangle, new Rectangle { Width = 14, Height = 12, Stroke = IconBrush, StrokeThickness = 1.6 }, "3");
        AddShapeButton(ShapeKind.FilledRectangle, new Rectangle { Width = 14, Height = 12, Fill = IconBrush }, "4");
        AddShapeButton(ShapeKind.Ellipse, new Ellipse { Width = 15, Height = 13, Stroke = IconBrush, StrokeThickness = 1.6 }, "5");
        AddShapeButton(ShapeKind.FilledEllipse, new Ellipse { Width = 15, Height = 13, Fill = IconBrush }, "6");

        Root.KeyDown += Root_KeyDown;
        UpdateToolButtons();
    }

    private static Brush IconBrush => (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];

    private static FontIcon Glyph(string glyph) => new() { Glyph = glyph, FontSize = 16 };

    private static UIElement DropIcon()
    {
        // しずくの形（塗りつぶし）
        var grid = new Grid { Width = 16, Height = 18 };
        grid.Children.Add(new Polygon
        {
            Points = { new Point(8, 1), new Point(13.5, 10), new Point(2.5, 10) },
            Fill = IconBrush,
        });
        grid.Children.Add(new Ellipse { Width = 12, Height = 12, Margin = new Thickness(2, 5, 2, 1), Fill = IconBrush });
        return grid;
    }

    private static UIElement FreehandIcon() => new Polyline
    {
        Points = { new Point(2, 12), new Point(5, 6), new Point(8, 11), new Point(11, 5), new Point(14, 10), new Point(16, 7) },
        Stroke = IconBrush,
        StrokeThickness = 1.6,
    };

    private void AddToolButton(Tool tool, UIElement icon, string key)
    {
        ToggleButton button = MakeToggle(icon, $"{ToolName(tool)} ({key})");
        button.Click += (_, _) => SelectTool(tool);
        _toolButtons[tool] = button;
        ToolGrid.Children.Add(button);
    }

    private void AddShapeButton(ShapeKind shape, UIElement icon, string key)
    {
        ToggleButton button = MakeToggle(icon, $"{ShapeName(shape)} ({key})");
        button.Click += (_, _) => SelectShape(shape);
        _shapeButtons[shape] = button;
        ShapeGrid.Children.Add(button);
    }

    private static ToggleButton MakeToggle(UIElement icon, string tip)
    {
        var button = new ToggleButton
        {
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            Content = icon,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(button, tip);
        return button;
    }

    private void SelectTool(Tool tool)
    {
        if (tool != Tool.Select)
        {
            CommitFloating(CurrentTab); // 選択ツールから離れたら、持ち上げ中の中身を置く
        }

        EndStroke();
        _tool = tool;
        UpdateToolButtons();
    }

    private void SelectShape(ShapeKind shape)
    {
        EndStroke();
        _shape = shape;
        UpdateToolButtons();
    }

    private void UpdateToolButtons()
    {
        foreach (var (tool, button) in _toolButtons)
        {
            button.IsChecked = tool == _tool;
        }

        // 塗りつぶしとスポイトには描き方がない
        bool usesShape = _tool is Tool.Pencil or Tool.Eraser;
        foreach (var (shape, button) in _shapeButtons)
        {
            button.IsChecked = shape == _shape;
            button.IsEnabled = usesShape;
        }

        UpdateStatus(null);
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || IsModifierDown(VirtualKey.Control) || IsModifierDown(VirtualKey.Menu))
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.B: SelectTool(Tool.Pencil); break;
            case VirtualKey.E: SelectTool(Tool.Eraser); break;
            case VirtualKey.G: SelectTool(Tool.Fill); break;
            case VirtualKey.I: SelectTool(Tool.Picker); break;
            case VirtualKey.M: SelectTool(Tool.Select); break;
            case VirtualKey.Escape: Deselect(); break;
            case VirtualKey.Delete when DeleteSelection(): break;
            case VirtualKey.Left when NudgeSelection(-NudgeStep(), 0): break;
            case VirtualKey.Right when NudgeSelection(NudgeStep(), 0): break;
            case VirtualKey.Up when NudgeSelection(0, -NudgeStep()): break;
            case VirtualKey.Down when NudgeSelection(0, NudgeStep()): break;
            case VirtualKey.Number1: SelectShape(ShapeKind.Freehand); break;
            case VirtualKey.Number2: SelectShape(ShapeKind.Line); break;
            case VirtualKey.Number3: SelectShape(ShapeKind.Rectangle); break;
            case VirtualKey.Number4: SelectShape(ShapeKind.FilledRectangle); break;
            case VirtualKey.Number5: SelectShape(ShapeKind.Ellipse); break;
            case VirtualKey.Number6: SelectShape(ShapeKind.FilledEllipse); break;
            case VirtualKey.X: SwapColors(); break;
            case VirtualKey.D: ResetColors(); break;
            case (VirtualKey)219: ChangeBrushSize(-1); break; // [
            case (VirtualKey)221: ChangeBrushSize(1); break;  // ]
            default: return;
        }

        e.Handled = true;
    }

    /// <summary>矢印キーで動かす量。Shift を押していれば 8px。</summary>
    private static int NudgeStep() => IsModifierDown(VirtualKey.Shift) ? 8 : 1;

    private static bool IsModifierDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    // ---- 押したときの振り分け ----

    /// <summary>描画系の押下を処理する。描き始めたら true（ポインタをキャプチャする）。</summary>
    private bool BeginToolAction(DocumentTab tab, (int X, int Y) p, bool erase)
    {
        if (_tool == Tool.Select)
        {
            BeginSelectAction(tab, p);
            return true;
        }

        uint color = erase || _tool == Tool.Eraser ? 0u : _color;

        if (_tool == Tool.Picker)
        {
            PickColor(tab, p);
            return false;
        }

        if (_tool == Tool.Fill)
        {
            PixelStroke fill = tab.Document.BeginStroke(_mask);
            if (fill.FloodFill(p.X, p.Y, color) > 0)
            {
                fill.Commit();
                AfterHistoryChange(tab);
            }

            return false;
        }

        _paintColor = color;
        _strokeTab = tab;
        if (_shape == ShapeKind.Freehand)
        {
            _drag = DragMode.Paint;
            _stroke = tab.Document.BeginStroke(_mask);
            (_lastX, _lastY) = p;
            Paint(tab, [p]);
        }
        else
        {
            _drag = DragMode.Shape;
            _shapeStart = p;
            _shapeEnd = p;
            Canvas.Invalidate();
        }

        return true;
    }

    private void PickColor(DocumentTab tab, (int X, int Y) p)
    {
        if (tab.Document.ActiveLayer.Image.Contains(p.X, p.Y))
        {
            SetCurrentColor(tab.Document.Composite().GetPixel(p.X, p.Y));
        }
    }

    private void UpdateShapeEnd((int X, int Y) p, bool constrain)
    {
        if (constrain)
        {
            p = ShapeRaster.Constrain(_shapeStart.X, _shapeStart.Y, p.X, p.Y, isLine: _shape == ShapeKind.Line);
        }

        if (p != _shapeEnd)
        {
            _shapeEnd = p;
            Canvas.Invalidate();
        }
    }

    private List<PixelSpan> CurrentShapeSpans()
    {
        (int x0, int y0) = _shapeStart;
        (int x1, int y1) = _shapeEnd;
        List<PixelSpan> spans = _shape switch
        {
            ShapeKind.Line => ShapeRaster.Line(x0, y0, x1, y1),
            ShapeKind.Rectangle => ShapeRaster.Rectangle(x0, y0, x1, y1, filled: false),
            ShapeKind.FilledRectangle => ShapeRaster.Rectangle(x0, y0, x1, y1, filled: true),
            ShapeKind.Ellipse => ShapeRaster.Ellipse(x0, y0, x1, y1, filled: false),
            ShapeKind.FilledEllipse => ShapeRaster.Ellipse(x0, y0, x1, y1, filled: true),
            _ => [],
        };

        // 線（直線・四角・円の枠）はブラシの太さで描く。塗りの図形はそのまま
        bool outline = _shape is ShapeKind.Line or ShapeKind.Rectangle or ShapeKind.Ellipse;
        return outline && _brush.Size > 1 ? ShapeRaster.ToSpans(_brush.Stamp(SpanPixels(spans))) : spans;
    }

    private static IEnumerable<(int X, int Y)> SpanPixels(IEnumerable<PixelSpan> spans)
    {
        foreach (PixelSpan s in spans)
        {
            for (int x = s.X0; x <= s.X1; x++)
            {
                yield return (x, s.Y);
            }
        }
    }

    /// <summary>図形を確定して1回の操作として履歴に積む。</summary>
    private void CommitShape()
    {
        if (_strokeTab is not { } tab)
        {
            return;
        }

        PixelStroke stroke = tab.Document.BeginStroke(_mask);
        if (stroke.PlotSpans(CurrentShapeSpans(), _paintColor))
        {
            stroke.Commit();
            AfterHistoryChange(tab);
        }
        else
        {
            Canvas.Invalidate();
        }
    }

    /// <summary>ドラッグ中の図形を、確定前のプレビューとしてキャンバスに重ねる。</summary>
    private void DrawShapePreview(CanvasDrawingSession ds, DocumentTab tab, Rect imageRect, float cell)
    {
        if (_drag != DragMode.Shape || _strokeTab != tab)
        {
            return;
        }

        Color color = _paintColor == 0 ? EraserPreviewColor : ToColor(_paintColor);
        int width = tab.Document.Width;
        foreach (PixelSpan s in CurrentShapeSpans())
        {
            if (s.Y < 0 || s.Y >= tab.Document.Height)
            {
                continue;
            }

            int x0 = Math.Max(s.X0, 0);
            int x1 = Math.Min(s.X1, width - 1);
            if (x0 > x1)
            {
                continue;
            }

            if (IsFlipped)
            {
                (x0, x1) = (width - 1 - x1, width - 1 - x0);
            }

            ds.FillRectangle(
                (float)imageRect.X + x0 * cell,
                (float)imageRect.Y + s.Y * cell,
                (x1 - x0 + 1) * cell,
                cell,
                color);
        }
    }
}
