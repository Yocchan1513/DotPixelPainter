using DotPixelPainter.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace DotPixelPainter;

/// <summary>
/// 選択ツール。ドラッグで四角く囲み、範囲の内側をドラッグすると中身を持ち上げて動かす。
/// 持ち上げた中身は、範囲の外をクリック・Esc・道具の切り替えなどで確定する（移動全体が1回の「元に戻す」）。
/// </summary>
public sealed partial class MainWindow
{
    private (int X, int Y) _selectStart;
    private (int X, int Y) _moveLast;

    /// <summary>選択ツールで押したとき。範囲の内側なら移動、外側なら新しく囲み始める。</summary>
    private void BeginSelectAction(DocumentTab tab, (int X, int Y) p)
    {
        PixelRect? current = tab.Floating?.Bounds ?? tab.Selection;
        if (current is { } r && r.Contains(p.X, p.Y))
        {
            EnsureLifted(tab);
            _drag = DragMode.MoveSelection;
            _moveLast = p;
            return;
        }

        CommitFloating(tab);
        _drag = DragMode.SelectRect;
        _selectStart = p;
        tab.Selection = PixelRect.FromPoints(p.X, p.Y, p.X, p.Y);
        Canvas.Invalidate();
    }

    private void UpdateSelectDrag(DocumentTab tab, (int X, int Y) p)
    {
        if (_drag == DragMode.SelectRect)
        {
            tab.Selection = PixelRect.FromPoints(_selectStart.X, _selectStart.Y, p.X, p.Y);
            Canvas.Invalidate();
        }
        else if (_drag == DragMode.MoveSelection && tab.Floating is { } floating)
        {
            int dx = p.X - _moveLast.X;
            int dy = p.Y - _moveLast.Y;
            if (dx != 0 || dy != 0)
            {
                floating.MoveBy(dx, dy);
                tab.Selection = floating.Bounds;
                _moveLast = p;
                tab.ImageChanged = true;
                Canvas.Invalidate();
                PreviewCanvas.Invalidate();
            }
        }
    }

    private void EndSelectDrag(DocumentTab tab, (int X, int Y) p)
    {
        if (_drag == DragMode.SelectRect)
        {
            // 動かさずにクリックしただけなら選択を解除。囲んだ範囲は画像の中に収める
            tab.Selection = p == _selectStart ? null : tab.Selection?.Intersect(ImageRect(tab));
            Canvas.Invalidate();
        }

        _drag = DragMode.None;
    }

    private static PixelRect ImageRect(DocumentTab tab) => new(0, 0, tab.Document.Width, tab.Document.Height);

    /// <summary>まだ持ち上げていなければ、選択範囲の中身をアクティブなレイヤーから持ち上げる。</summary>
    private void EnsureLifted(DocumentTab tab)
    {
        if (tab.Floating is not null || tab.Selection is not { } rect)
        {
            return;
        }

        PixelStroke stroke = tab.Document.BeginStroke();
        FloatingSelection? floating = FloatingSelection.Lift(stroke, tab.Document.ActiveLayer.Image, rect);
        if (floating is null)
        {
            return;
        }

        tab.Floating = floating;
        tab.FloatStroke = stroke;
        tab.ImageChanged = true;
    }

    /// <summary>持ち上げた中身を今の位置に置いて、移動全体を1回分として履歴に積む。</summary>
    private void CommitFloating(DocumentTab? tab)
    {
        if (tab?.Floating is not { } floating || tab.FloatStroke is not { } stroke)
        {
            return;
        }

        floating.Drop(stroke);
        stroke.Commit();
        tab.Selection = floating.Bounds.Intersect(ImageRect(tab));
        tab.Floating = null;
        tab.FloatStroke = null;
        if (tab == CurrentTab)
        {
            AfterHistoryChange(tab);
        }
        else
        {
            tab.ImageChanged = true;
            RefreshHeader(tab);
        }
    }

    private void CommitAllFloating()
    {
        foreach (DocumentTab tab in _tabs.Values)
        {
            CommitFloating(tab);
        }
    }

    // ---- キー操作 ----

    private void SelectAll()
    {
        if (CurrentTab is { } tab)
        {
            CommitFloating(tab);
            tab.Selection = ImageRect(tab);
            SelectTool(Tool.Select);
            Canvas.Invalidate();
        }
    }

    private void Deselect()
    {
        if (CurrentTab is { } tab && (tab.Selection is not null || tab.Floating is not null))
        {
            CommitFloating(tab);
            tab.Selection = null;
            Canvas.Invalidate();
        }
    }

    /// <summary>選択範囲の中を消す。持ち上げ中なら、持ち上げた中身を捨てる。</summary>
    private bool DeleteSelection()
    {
        if (CurrentTab is not { } tab)
        {
            return false;
        }

        if (tab.Floating is not null && tab.FloatStroke is { } floatStroke)
        {
            floatStroke.Commit(); // 元の場所を透明にした分だけが残る
            tab.Floating = null;
            tab.FloatStroke = null;
            AfterHistoryChange(tab);
            return true;
        }

        if (tab.Selection is not { } rect)
        {
            return false;
        }

        PixelStroke stroke = tab.Document.BeginStroke();
        if (stroke.PlotSpans(Enumerable.Range(rect.Y, rect.Height).Select(y => new PixelSpan(y, rect.X, rect.Right - 1)), 0))
        {
            stroke.Commit();
            AfterHistoryChange(tab);
        }

        return true;
    }

    /// <summary>矢印キーで選択範囲の中身を動かす。選択していなければ何もしない（false）。</summary>
    private bool NudgeSelection(int dx, int dy)
    {
        if (CurrentTab is not { } tab || (tab.Selection is null && tab.Floating is null))
        {
            return false;
        }

        EnsureLifted(tab);
        if (tab.Floating is { } floating)
        {
            floating.MoveBy(dx, dy);
            tab.Selection = floating.Bounds;
            tab.ImageChanged = true;
            Canvas.Invalidate();
            PreviewCanvas.Invalidate();
        }

        return true;
    }

    // ---- 表示 ----

    /// <summary>選択範囲の枠（黒い線の上に白い破線。どの色の上でも見える）。</summary>
    private void DrawSelectionFrame(CanvasDrawingSession ds, DocumentTab tab, Rect imageRect, float cell, float stroke)
    {
        if ((tab.Floating?.Bounds ?? tab.Selection) is not { } r)
        {
            return;
        }

        int x = IsFlipped ? tab.Document.Width - r.Right : r.X;
        var frame = new Rect(
            imageRect.X + x * cell + stroke / 2,
            imageRect.Y + r.Y * cell + stroke / 2,
            r.Width * cell - stroke,
            r.Height * cell - stroke);
        ds.DrawRectangle(frame, Color.FromArgb(255, 0, 0, 0), stroke * 2);
        using var dashed = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash };
        ds.DrawRectangle(frame, Color.FromArgb(255, 255, 255, 255), stroke * 2, dashed);
    }
}
