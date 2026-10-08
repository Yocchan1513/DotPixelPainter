using System.Numerics;
using DotPixelPainter.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace DotPixelPainter;

public sealed partial class MainWindow : Window
{
    private static readonly Color GridColor = Color.FromArgb(110, 64, 64, 64);
    private static readonly Color FrameColor = Color.FromArgb(255, 96, 96, 96);

    private readonly StartupProbe _probe;
    private readonly Dictionary<TabViewItem, DocumentTab> _tabs = [];

    private CanvasBitmap? _bitmap;
    private DocumentTab? _bitmapOwner;
    private CanvasImageBrush? _checker;
    private byte[] _pixelBuffer = [];
    private int _untitledCount;
    private bool _firstFrameReported;
    private bool _forceClose;

    private DragMode _drag;
    private PixelStroke? _stroke;
    private DocumentTab? _strokeTab;
    private uint _paintColor;
    private int _lastX;
    private int _lastY;
    private Point _panStart;
    private float _panStartX;
    private float _panStartY;

    public MainWindow(StartupProbe probe)
    {
        _probe = probe;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        AppWindow.Closing += AppWindow_Closing;
        Root.Loaded += (_, _) => UpdateTitleBarInset();
        Root.SizeChanged += (_, _) => UpdateTitleBarInset();

        InitializePalette();
        BuildToolPanel();
        AddKeyboardShortcuts();
        AddTab(CreateUntitled());
    }

    private enum DragMode
    {
        None,
        Paint,
        Pan,
        Shape,
    }

    private DocumentTab? CurrentTab =>
        Tabs.SelectedItem is TabViewItem item && _tabs.TryGetValue(item, out var tab) ? tab : null;

    private bool IsFlipped => FlipToggle.IsChecked == true;

    // ---- タブ ----

    private PixelDocument CreateUntitled()
    {
        _untitledCount++;
        return new PixelDocument($"無題 {_untitledCount}", 64, 64);
    }

    private void AddTab(PixelDocument document)
    {
        var tab = new DocumentTab(document);
        var item = new TabViewItem { Header = tab.Header };
        _tabs[item] = tab;
        Tabs.TabItems.Add(item);
        Tabs.SelectedItem = item;
    }

    private void RefreshHeader(DocumentTab tab)
    {
        foreach (var (item, t) in _tabs)
        {
            if (t == tab)
            {
                item.Header = tab.Header;
            }
        }
    }

    private async Task CloseTabAsync(TabViewItem item)
    {
        DocumentTab tab = _tabs[item];
        if (tab.Document.IsDirty)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = $"「{tab.Document.Name}」は保存されていません",
                Content = "閉じる前に保存しますか？",
                PrimaryButtonText = "保存",
                SecondaryButtonText = "保存しない",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Primary,
            };
            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.None)
            {
                return;
            }

            if (result == ContentDialogResult.Primary && !await SaveAsync(tab))
            {
                return;
            }
        }

        Tabs.TabItems.Remove(item);
        _tabs.Remove(item);
        if (_bitmapOwner == tab)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            _bitmapOwner = null;
        }

        if (Tabs.TabItems.Count == 0)
        {
            AddTab(CreateUntitled());
        }
    }

    private void SelectRelativeTab(int direction)
    {
        int count = Tabs.TabItems.Count;
        if (count > 1)
        {
            Tabs.SelectedIndex = (Tabs.SelectedIndex + direction + count) % count;
        }
    }

    private void Tabs_AddTabButtonClick(TabView sender, object args) => AddTab(CreateUntitled());

    private async void Tabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) =>
        await CloseTabAsync((TabViewItem)args.Tab);

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        EndStroke();
        RedrawAll();
        UpdateUndoButtons();
    }

    private void UpdateTitleBarInset()
    {
        double scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
        DragRegion.MinWidth = AppWindow.TitleBar.RightInset / scale + 48;
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_forceClose || !_tabs.Values.Any(t => t.Document.IsDirty))
        {
            return;
        }

        args.Cancel = true;
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "保存されていないタブがあります",
            Content = "保存せずに終了しますか？",
            PrimaryButtonText = "終了する",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _forceClose = true;
            Close();
        }
    }

    // ---- ツールバー ----

    private void AddKeyboardShortcuts()
    {
        AddShortcut(VirtualKey.Z, VirtualKeyModifiers.Control, Undo);
        AddShortcut(VirtualKey.Y, VirtualKeyModifiers.Control, Redo);
        AddShortcut(VirtualKey.Z, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, Redo);
        AddShortcut(VirtualKey.N, VirtualKeyModifiers.Control, () => AddTab(CreateUntitled()));
        AddShortcut(VirtualKey.O, VirtualKeyModifiers.Control, () => _ = OpenAsync());
        AddShortcut(VirtualKey.S, VirtualKeyModifiers.Control, () => _ = SaveCurrentAsync());
        AddShortcut(VirtualKey.W, VirtualKeyModifiers.Control, () =>
        {
            if (Tabs.SelectedItem is TabViewItem item)
            {
                _ = CloseTabAsync(item);
            }
        });
        AddShortcut(VirtualKey.Tab, VirtualKeyModifiers.Control, () => SelectRelativeTab(1));
        AddShortcut(VirtualKey.Tab, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => SelectRelativeTab(-1));
    }

    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        Root.KeyboardAccelerators.Add(accelerator);
    }

    private void New_Click(object sender, RoutedEventArgs e) => AddTab(CreateUntitled());

    private async void Open_Click(object sender, RoutedEventArgs e) => await OpenAsync();

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveCurrentAsync();

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomAtCenter(1);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomAtCenter(-1);

    private void ViewOption_Click(object sender, RoutedEventArgs e) => RedrawAll();

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    // ---- 元に戻す／やり直し ----

    private void Undo()
    {
        EndStroke();
        if (CurrentTab is { } tab && tab.Document.History.Undo())
        {
            AfterHistoryChange(tab);
        }
    }

    private void Redo()
    {
        EndStroke();
        if (CurrentTab is { } tab && tab.Document.History.Redo())
        {
            AfterHistoryChange(tab);
        }
    }

    private void AfterHistoryChange(DocumentTab tab)
    {
        tab.ImageChanged = true;
        RefreshHeader(tab);
        UpdateUndoButtons();
        Canvas.Invalidate();
        PreviewCanvas.Invalidate();
    }

    private void UpdateUndoButtons()
    {
        UndoHistory? history = CurrentTab?.Document.History;
        UndoButton.IsEnabled = history?.CanUndo == true;
        RedoButton.IsEnabled = history?.CanRedo == true;
    }

    // ---- ファイル ----

    private async Task OpenAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".png");
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            AddTab(await PngFile.LoadAsync(file));
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("開けませんでした", ex.Message);
        }
    }

    private async Task SaveCurrentAsync()
    {
        if (CurrentTab is { } tab)
        {
            await SaveAsync(tab);
        }
    }

    private async Task<bool> SaveAsync(DocumentTab tab)
    {
        PixelDocument document = tab.Document;
        StorageFile? file = null;
        try
        {
            if (document.FilePath is not null)
            {
                file = await StorageFile.GetFileFromPathAsync(document.FilePath);
            }
        }
        catch (Exception)
        {
            file = null;
        }

        if (file is null)
        {
            var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(document.Name) };
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("PNG 画像", [".png"]);
            file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return false;
            }
        }

        try
        {
            await PngFile.SaveAsync(document, file);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("保存できませんでした", ex.Message);
            return false;
        }

        document.FilePath = file.Path;
        document.Name = file.Name;
        document.MarkSaved();
        RefreshHeader(tab);
        return true;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "OK",
        };
        await dialog.ShowAsync();
    }

    // ---- 描画 ----

    private void RedrawAll()
    {
        Canvas.Invalidate();
        PreviewCanvas.Invalidate();
        UpdateStatus(null);
    }

    private CanvasBitmap GetBitmap(ICanvasResourceCreator resourceCreator, DocumentTab tab)
    {
        PixelDocument document = tab.Document;
        if (_bitmap is not null && _bitmapOwner == tab && !tab.ImageChanged && _bitmap.Device == resourceCreator.Device)
        {
            return _bitmap;
        }

        PixelImage composite = document.Composite();
        int length = document.Width * document.Height * 4;
        if (_pixelBuffer.Length != length)
        {
            _pixelBuffer = new byte[length];
        }

        composite.CopyToBgraPremultiplied(_pixelBuffer);

        if (_bitmap is null
            || _bitmap.Device != resourceCreator.Device
            || _bitmap.SizeInPixels.Width != document.Width
            || _bitmap.SizeInPixels.Height != document.Height)
        {
            _bitmap?.Dispose();
            _bitmap = CanvasBitmap.CreateFromBytes(
                resourceCreator,
                _pixelBuffer,
                document.Width,
                document.Height,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                96f,
                CanvasAlphaMode.Premultiplied);
        }
        else
        {
            _bitmap.SetPixelBytes(_pixelBuffer);
        }

        _bitmapOwner = tab;
        tab.ImageChanged = false;
        return _bitmap;
    }

    private CanvasImageBrush GetChecker(ICanvasResourceCreator resourceCreator)
    {
        if (_checker is not null && _checker.Device == resourceCreator.Device)
        {
            return _checker;
        }

        var tile = new CanvasRenderTarget(resourceCreator, 16, 16, 96f);
        using (CanvasDrawingSession ds = tile.CreateDrawingSession())
        {
            ds.Clear(Color.FromArgb(255, 255, 255, 255));
            ds.FillRectangle(8, 0, 8, 8, Color.FromArgb(255, 232, 232, 232));
            ds.FillRectangle(0, 8, 8, 8, Color.FromArgb(255, 232, 232, 232));
        }

        _checker?.Dispose();
        _checker = new CanvasImageBrush(resourceCreator, tile)
        {
            ExtendX = CanvasEdgeBehavior.Wrap,
            ExtendY = CanvasEdgeBehavior.Wrap,
            Interpolation = CanvasImageInterpolation.NearestNeighbor,
        };
        return _checker;
    }

    /// <summary>画像左上の位置（物理ピクセル）。整数に揃えてドットがにじまないようにする。</summary>
    private static (float Scale, float OriginX, float OriginY) Layout(CanvasControl canvas, DocumentTab tab)
    {
        float scale = canvas.Dpi / 96f;
        float viewW = (float)canvas.ActualWidth * scale;
        float viewH = (float)canvas.ActualHeight * scale;
        float imageW = tab.Document.Width * tab.Zoom;
        float imageH = tab.Document.Height * tab.Zoom;
        float ox = MathF.Floor((viewW - imageW) / 2 + tab.PanX);
        float oy = MathF.Floor((viewH - imageH) / 2 + tab.PanY);
        return (scale, ox, oy);
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        DocumentTab? tab = CurrentTab;
        if (tab is null)
        {
            return;
        }

        PixelDocument document = tab.Document;
        CanvasDrawingSession ds = args.DrawingSession;
        ds.Antialiasing = CanvasAntialiasing.Aliased;

        CanvasBitmap bitmap = GetBitmap(sender, tab);
        (float scale, float ox, float oy) = Layout(sender, tab);
        var rect = new Rect(ox / scale, oy / scale, document.Width * tab.Zoom / scale, document.Height * tab.Zoom / scale);

        CanvasImageBrush checker = GetChecker(sender);
        checker.Transform = Matrix3x2.CreateTranslation((float)rect.X, (float)rect.Y);
        ds.FillRectangle(rect, checker);

        if (IsFlipped)
        {
            ds.Transform = Matrix3x2.CreateScale(-1, 1, new Vector2((float)(rect.X + rect.Width / 2), 0));
        }

        ds.DrawImage(bitmap, rect, new Rect(0, 0, document.Width, document.Height), 1f, CanvasImageInterpolation.NearestNeighbor);
        ds.Transform = Matrix3x2.Identity;

        DrawShapePreview(ds, tab, rect, tab.Zoom / scale);

        if (GridToggle.IsChecked == true && tab.Zoom >= 4)
        {
            DrawPixelGrid(ds, rect, document, tab.Zoom / scale, 1 / scale, (float)sender.ActualWidth, (float)sender.ActualHeight);
        }

        ds.DrawRectangle(rect, FrameColor, 1 / scale);

        if (!_firstFrameReported)
        {
            _firstFrameReported = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_probe.ReportFirstFrame())
                {
                    _forceClose = true;
                    Close();
                }
            });
        }
    }

    /// <summary>1pxグリッド線。画面に見えている範囲の線だけを、物理1ピクセル幅で描く。</summary>
    private static void DrawPixelGrid(CanvasDrawingSession ds, Rect r, PixelDocument document, float cell, float stroke, float viewW, float viewH)
    {
        float left = (float)r.X;
        float top = (float)r.Y;
        float bottom = (float)(r.Y + r.Height);
        float right = (float)(r.X + r.Width);
        float half = stroke / 2;

        int x0 = Math.Max(1, (int)MathF.Ceiling(-left / cell));
        int x1 = Math.Min(document.Width - 1, (int)MathF.Floor((viewW - left) / cell));
        for (int x = x0; x <= x1; x++)
        {
            float px = left + x * cell + half;
            ds.DrawLine(px, Math.Max(top, 0), px, Math.Min(bottom, viewH), GridColor, stroke);
        }

        int y0 = Math.Max(1, (int)MathF.Ceiling(-top / cell));
        int y1 = Math.Min(document.Height - 1, (int)MathF.Floor((viewH - top) / cell));
        for (int y = y0; y <= y1; y++)
        {
            float py = top + y * cell + half;
            ds.DrawLine(Math.Max(left, 0), py, Math.Min(right, viewW), py, GridColor, stroke);
        }
    }

    private void PreviewCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        DocumentTab? tab = CurrentTab;
        if (tab is null)
        {
            return;
        }

        PixelDocument document = tab.Document;
        CanvasDrawingSession ds = args.DrawingSession;
        ds.Antialiasing = CanvasAntialiasing.Aliased;
        CanvasBitmap bitmap = GetBitmap(sender, tab);
        CanvasImageBrush checker = GetChecker(sender);
        float scale = sender.Dpi / 96f;
        float availW = (float)sender.ActualWidth;
        float y = 0;

        foreach (int times in (ReadOnlySpan<int>)[1, 2])
        {
            float w = document.Width * times / scale;
            float h = document.Height * times / scale;
            if (w > availW)
            {
                h *= availW / w;
                w = availW;
            }

            var rect = new Rect(0, y, w, h);
            checker.Transform = Matrix3x2.CreateTranslation(0, y);
            ds.FillRectangle(rect, checker);
            if (IsFlipped)
            {
                ds.Transform = Matrix3x2.CreateScale(-1, 1, new Vector2(w / 2, 0));
            }

            ds.DrawImage(bitmap, rect, new Rect(0, 0, document.Width, document.Height), 1f, CanvasImageInterpolation.NearestNeighbor);
            ds.Transform = Matrix3x2.Identity;
            y = MathF.Ceiling((y + h) * scale + 16) / scale;
        }
    }

    // ---- 入力 ----

    private (int X, int Y) ToImage(DocumentTab tab, Point p)
    {
        (float scale, float ox, float oy) = Layout(Canvas, tab);
        int x = (int)MathF.Floor(((float)p.X * scale - ox) / tab.Zoom);
        int y = (int)MathF.Floor(((float)p.Y * scale - oy) / tab.Zoom);
        if (IsFlipped)
        {
            x = tab.Document.Width - 1 - x;
        }

        return (x, y);
    }

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (CurrentTab is not { } tab)
        {
            return;
        }

        var point = e.GetCurrentPoint(Canvas);
        var props = point.Properties;
        if (props.IsMiddleButtonPressed)
        {
            _drag = DragMode.Pan;
            _panStart = point.Position;
            _panStartX = tab.PanX;
            _panStartY = tab.PanY;
        }
        else if (props.IsLeftButtonPressed && e.KeyModifiers.HasFlag(VirtualKeyModifiers.Menu))
        {
            // Alt+クリックは、どの道具でもスポイト（表示中の色を拾う）
            PickColor(tab, ToImage(tab, point.Position));
            e.Handled = true;
            return;
        }
        else if (props.IsLeftButtonPressed || props.IsRightButtonPressed || props.IsEraser)
        {
            // 右クリックとペンの消しゴム側は「透明で描く」
            EndStroke();
            bool erase = props.IsRightButtonPressed || props.IsEraser;
            if (!BeginToolAction(tab, ToImage(tab, point.Position), erase))
            {
                e.Handled = true;
                return;
            }
        }
        else
        {
            return;
        }

        Canvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (CurrentTab is not { } tab)
        {
            return;
        }

        var point = e.GetCurrentPoint(Canvas);
        (int x, int y) = ToImage(tab, point.Position);
        UpdateStatus((x, y));

        if (_drag == DragMode.Paint)
        {
            if (x != _lastX || y != _lastY)
            {
                Paint(tab, PixelLine.Enumerate(_lastX, _lastY, x, y));
                (_lastX, _lastY) = (x, y);
            }
        }
        else if (_drag == DragMode.Shape)
        {
            UpdateShapeEnd((x, y), e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift));
        }
        else if (_drag == DragMode.Pan)
        {
            float scale = Canvas.Dpi / 96f;
            tab.PanX = _panStartX + (float)(point.Position.X - _panStart.X) * scale;
            tab.PanY = _panStartY + (float)(point.Position.Y - _panStart.Y) * scale;
            Canvas.Invalidate();
        }
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == DragMode.Shape)
        {
            _drag = DragMode.None;
            CommitShape();
            _strokeTab = null;
        }

        EndStroke();
        Canvas.ReleasePointerCapture(e.Pointer);
    }

    private void Canvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndStroke();

    /// <summary>
    /// 描いている途中のひと筆を確定して履歴に積む。
    /// 確定前の図形（ドラッグ中のプレビュー）は取り消す。
    /// </summary>
    private void EndStroke()
    {
        if (_drag == DragMode.Shape)
        {
            _strokeTab = null;
            Canvas.Invalidate();
        }

        _drag = DragMode.None;
        if (_stroke is null || _strokeTab is null)
        {
            return;
        }

        _stroke.Commit();
        RefreshHeader(_strokeTab);
        _stroke = null;
        _strokeTab = null;
        UpdateUndoButtons();
    }

    private void Canvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (CurrentTab is not { } tab)
        {
            return;
        }

        var point = e.GetCurrentPoint(Canvas);
        int delta = point.Properties.MouseWheelDelta;
        ZoomAt(tab, tab.NextZoom(delta > 0 ? 1 : -1), point.Position);
        e.Handled = true;
    }

    private void Paint(DocumentTab tab, IEnumerable<(int X, int Y)> points)
    {
        if (_stroke is null)
        {
            return;
        }

        bool changed = false;
        foreach ((int x, int y) in points)
        {
            changed |= _stroke.Plot(x, y, _paintColor);
        }

        if (!changed)
        {
            return;
        }

        tab.ImageChanged = true;
        Canvas.Invalidate();
        PreviewCanvas.Invalidate();
    }

    private void ZoomAtCenter(int direction)
    {
        if (CurrentTab is { } tab)
        {
            ZoomAt(tab, tab.NextZoom(direction), new Point(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2));
        }
    }

    /// <summary>指定位置（DIP）の下にあるピクセルが動かないように倍率を変える。</summary>
    private void ZoomAt(DocumentTab tab, int newZoom, Point anchor)
    {
        if (newZoom == tab.Zoom)
        {
            return;
        }

        (float scale, float ox, float oy) = Layout(Canvas, tab);
        float cx = (float)anchor.X * scale;
        float cy = (float)anchor.Y * scale;
        float u = (cx - ox) / tab.Zoom;
        float v = (cy - oy) / tab.Zoom;
        float viewW = (float)Canvas.ActualWidth * scale;
        float viewH = (float)Canvas.ActualHeight * scale;

        tab.Zoom = newZoom;
        tab.PanX = cx - u * newZoom - (viewW - tab.Document.Width * newZoom) / 2;
        tab.PanY = cy - v * newZoom - (viewH - tab.Document.Height * newZoom) / 2;
        Canvas.Invalidate();
        UpdateStatus(null);
    }

    private void UpdateStatus((int X, int Y)? position)
    {
        if (CurrentTab is not { } tab)
        {
            return;
        }

        ZoomText.Text = $"{tab.Zoom * 100}%";
        string pos = position is { } p && tab.Document.ActiveLayer.Image.Contains(p.X, p.Y) ? $"{p.X}, {p.Y}" : "-";
        StatusText.Text = $"{tab.Document.Width} × {tab.Document.Height}　｜　倍率 {tab.Zoom * 100}%　｜　座標 {pos}　｜　{ToolStatus()}　｜　{tab.Document.ActiveLayer.Name}";
    }

    private static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
