using System.Numerics;
using DotPixelPainter.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotPixelPainter;

/// <summary>
/// 画面の分割。右に資料（開いている別のタブ、または資料として開いた画像）を表示する。
/// 右は見るだけ: ホイールで拡大縮小、ドラッグで移動、クリックで色を拾う、ダブルクリックで全体を表示。
/// 右の部品は分割したときに初めてコードで作る（起動を軽くするため）。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>資料として開いた画像（タブには加えない）。</summary>
    private sealed record ReferenceImage(string Name, PixelImage Image);

    private readonly List<ReferenceImage> _referenceImages = [];

    /// <summary>右に並べている選択肢（DocumentTab か ReferenceImage）。RefSourceBox の項目と同じ順。</summary>
    private readonly List<object> _refChoices = [];

    private object? _refSource;
    private CanvasBitmap? _refBitmap;
    private bool _refDirty = true;
    private double _refRatio = 0.4;

    // 表示の倍率（物理ピクセル / 画像の1ピクセル）と、真ん中からのずれ（物理ピクセル）
    private float _refZoom;
    private float _refPanX;
    private float _refPanY;
    private bool _refFit = true;

    private Point _refPressAt;
    private bool _refPressed;
    private bool _refPanning;

    // 分割したときに初めて作る部品
    private Grid? RefPane;
    private SideResizeGrip? RefSplitter;
    private ComboBox RefSourceBox = null!;
    private CanvasControl RefCanvas = null!;

    private bool SplitOpen => RefPane is { Visibility: Visibility.Visible };

    private void SplitToggle_Click(object sender, RoutedEventArgs e) => SetSplit(SplitToggle.IsChecked == true);

    private void SetSplit(bool open)
    {
        SplitToggle.IsChecked = open;
        if (open)
        {
            EnsureReferencePane();
            RefPane!.Visibility = Visibility.Visible;
            RefSplitter!.Visibility = Visibility.Visible;
            ApplySplitRatio();
            RefreshReferenceChoices();
            if (_refSource is null)
            {
                // 今のタブ以外の最初のタブを出しておく
                object? first = _refChoices.FirstOrDefault(c => c != CurrentTab);
                if (first is not null)
                {
                    RefSourceBox.SelectedIndex = _refChoices.IndexOf(first);
                }
            }
        }
        else if (RefPane is not null)
        {
            RefPane.Visibility = Visibility.Collapsed;
            RefSplitter!.Visibility = Visibility.Collapsed;
            CanvasArea.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            RefColumn.Width = new GridLength(0);
        }
    }

    /// <summary>右の画面（上に選ぶ欄とボタン、下に絵）と、左右の境目のつまみを作る。</summary>
    private void EnsureReferencePane()
    {
        if (RefPane is not null)
        {
            return;
        }

        RefSplitter = new SideResizeGrip
        {
            Width = 6,
            Background = (Microsoft.UI.Xaml.Media.Brush)RightPanel.BorderBrush,
        };
        ToolTipService.SetToolTip(RefSplitter, Texts.RefSplitterTip);
        RefSplitter.PointerPressed += RefSplitter_PointerPressed;
        RefSplitter.PointerMoved += RefSplitter_PointerMoved;
        RefSplitter.PointerReleased += RefSplitter_PointerReleased;
        RefSplitter.PointerCaptureLost += RefSplitter_PointerCaptureLost;
        Grid.SetColumn(RefSplitter, 1);

        RefSourceBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = Texts.RefChoose };
        ToolTipService.SetToolTip(RefSourceBox, Texts.RefSourceTip);
        RefSourceBox.DropDownOpened += RefSourceBox_DropDownOpened;
        RefSourceBox.SelectionChanged += RefSourceBox_SelectionChanged;

        Button IconButton(string glyph, string tip, RoutedEventHandler click)
        {
            var button = new Button { Padding = new Thickness(8, 6, 8, 6), Content = new FontIcon { Glyph = glyph, FontSize = 14 } };
            ToolTipService.SetToolTip(button, tip);
            button.Click += click;
            return button;
        }

        var header = new Grid
        {
            Padding = new Thickness(8, 4, 8, 4),
            ColumnSpacing = 4,
            Background = RightPanel.Background,
            BorderBrush = RightPanel.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Button open = IconButton("\uE8E5", Texts.RefOpenTip, (_, _) => ErrorLog.Run("資料を開く", OpenReferenceAsync));
        Button close = IconButton("\uE711", Texts.RefCloseTip, (_, _) => SetSplit(false));
        Grid.SetColumn(open, 1);
        Grid.SetColumn(close, 2);
        header.Children.Add(RefSourceBox);
        header.Children.Add(open);
        header.Children.Add(close);

        RefCanvas = new CanvasControl { ClearColor = Windows.UI.Color.FromArgb(255, 0x2B, 0x2B, 0x2B) };
        ToolTipService.SetToolTip(RefCanvas, Texts.RefCanvasTip);
        RefCanvas.Draw += RefCanvas_Draw;
        RefCanvas.PointerPressed += RefCanvas_PointerPressed;
        RefCanvas.PointerMoved += RefCanvas_PointerMoved;
        RefCanvas.PointerReleased += RefCanvas_PointerReleased;
        RefCanvas.PointerCaptureLost += RefCanvas_PointerCaptureLost;
        RefCanvas.PointerWheelChanged += RefCanvas_PointerWheelChanged;
        RefCanvas.DoubleTapped += RefCanvas_DoubleTapped;
        Grid.SetRow(RefCanvas, 1);

        RefPane = new Grid();
        RefPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RefPane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RefPane.Children.Add(header);
        RefPane.Children.Add(RefCanvas);
        Grid.SetColumn(RefPane, 2);

        CanvasArea.Children.Add(RefSplitter);
        CanvasArea.Children.Add(RefPane);
    }

    private void ApplySplitRatio()
    {
        // 左（描く画面）と右（資料）の幅の割合
        double right = Math.Clamp(_refRatio, 0.15, 0.85);
        CanvasArea.ColumnDefinitions[0].Width = new GridLength(1 - right, GridUnitType.Star);
        RefColumn.Width = new GridLength(right, GridUnitType.Star);
    }

    // ---- 表示するものを選ぶ ----

    private void RefSourceBox_DropDownOpened(object? sender, object e) => RefreshReferenceChoices();

    /// <summary>選択肢を、開いているタブ（タブの並び順）と資料の画像で作り直す。</summary>
    private void RefreshReferenceChoices()
    {
        if (RefSourceBox is null)
        {
            return;
        }

        _refChoices.Clear();
        foreach (object item in Tabs.TabItems)
        {
            if (item is TabViewItem tabItem && _tabs.TryGetValue(tabItem, out DocumentTab? tab))
            {
                _refChoices.Add(tab);
            }
        }

        _refChoices.AddRange(_referenceImages);

        _refSyncing = true;
        RefSourceBox.Items.Clear();
        foreach (object choice in _refChoices)
        {
            RefSourceBox.Items.Add(choice switch
            {
                DocumentTab tab => tab.Document.Name,
                ReferenceImage image => T($"資料: {image.Name}", $"Reference: {image.Name}"),
                _ => "",
            });
        }

        RefSourceBox.SelectedIndex = _refSource is null ? -1 : _refChoices.IndexOf(_refSource);
        _refSyncing = false;
    }

    private bool _refSyncing;

    private void RefSourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refSyncing || RefSourceBox.SelectedIndex < 0 || RefSourceBox.SelectedIndex >= _refChoices.Count)
        {
            return;
        }

        object choice = _refChoices[RefSourceBox.SelectedIndex];
        if (choice != _refSource)
        {
            _refSource = choice;
            _refFit = true;
            _refDirty = true;
            RefCanvas?.Invalidate();
        }
    }

    private async Task OpenReferenceAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        foreach (string ext in (ReadOnlySpan<string>)[".png", ".jpg", ".jpeg", ".bmp", ".gif", DotPixFile.Extension, PsdReader.Extension])
        {
            picker.FileTypeFilter.Add(ext);
        }

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            PixelImage image;
            string ext = Path.GetExtension(file.Path);
            if (IsDotPix(file.Path) || string.Equals(ext, PsdReader.Extension, StringComparison.OrdinalIgnoreCase))
            {
                (PixelDocument document, _) = await LoadDocumentAsync(file);
                image = document.Composite();
            }
            else
            {
                using var stream = await file.OpenReadAsync();
                image = await PngFile.DecodeAsync(stream, forReference: true);
            }

            var reference = new ReferenceImage(file.Name, image);
            _referenceImages.Add(reference);
            _refSource = reference;
            _refFit = true;
            _refDirty = true;
            RefreshReferenceChoices();
            RefCanvas.Invalidate();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(T("開けませんでした", "Could not open the file"), ex.Message);
        }
    }

    /// <summary>描いている絵が変わったら、右に同じタブを出していれば描き直す。</summary>
    private void RefreshReferenceView()
    {
        if (SplitOpen && _refSource is DocumentTab)
        {
            _refDirty = true;
            RefCanvas.Invalidate();
        }
    }

    /// <summary>右に出していたタブが閉じられたら、何も出さない状態にする。</summary>
    private void ForgetClosedReference()
    {
        if (_refSource is DocumentTab tab && !_tabs.ContainsValue(tab))
        {
            _refSource = null;
            _refDirty = true;
            if (SplitOpen)
            {
                RefreshReferenceChoices();
                RefCanvas.Invalidate();
            }
        }
    }

    // ---- 描く ----

    /// <summary>右に出す絵。タブが閉じられていたら null。</summary>
    private PixelImage? ReferencePixels() => _refSource switch
    {
        DocumentTab tab when _tabs.ContainsValue(tab) => tab.Document.Composite(),
        ReferenceImage image => image.Image,
        _ => null,
    };

    private CanvasBitmap? GetReferenceBitmap(ICanvasResourceCreator creator)
    {
        if (!_refDirty && _refBitmap is not null && _refBitmap.Device == creator.Device)
        {
            return _refBitmap;
        }

        _refBitmap?.Dispose();
        _refBitmap = null;
        _refDirty = false;
        if (ReferencePixels() is not { } image)
        {
            return null;
        }

        var bytes = new byte[image.Width * image.Height * 4];
        image.CopyToBgraPremultiplied(bytes);
        _refBitmap = CanvasBitmap.CreateFromBytes(creator, bytes, image.Width, image.Height, DirectXPixelFormat.B8G8R8A8UIntNormalized, 96f, CanvasAlphaMode.Premultiplied);
        return _refBitmap;
    }

    /// <summary>画像の左上の位置と倍率（どちらも物理ピクセル）。</summary>
    private (float Scale, float X, float Y) ReferenceLayout(CanvasControl canvas, int width, int height)
    {
        float dpi = canvas.Dpi / 96f;
        float viewW = (float)canvas.ActualWidth * dpi;
        float viewH = (float)canvas.ActualHeight * dpi;
        if (_refFit || _refZoom <= 0)
        {
            float fit = Math.Min(viewW / width, viewH / height) * 0.95f;
            _refZoom = fit >= 1 ? MathF.Floor(fit) : fit; // 拡大は整数倍にしてドットをくっきり見せる
            _refPanX = 0;
            _refPanY = 0;
        }

        float x = MathF.Round((viewW - width * _refZoom) / 2 + _refPanX);
        float y = MathF.Round((viewH - height * _refZoom) / 2 + _refPanY);
        return (dpi, x, y);
    }

    private void RefCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        CanvasDrawingSession ds = args.DrawingSession;
        if (GetReferenceBitmap(sender) is not { } bitmap)
        {
            var format = new Microsoft.Graphics.Canvas.Text.CanvasTextFormat
            {
                FontSize = 14,
                HorizontalAlignment = Microsoft.Graphics.Canvas.Text.CanvasHorizontalAlignment.Center,
                VerticalAlignment = Microsoft.Graphics.Canvas.Text.CanvasVerticalAlignment.Center,
            };
            ds.DrawText(
                T("上で、表示するタブを選ぶか、\n資料の画像を開いてください", "Choose a tab above,\nor open a reference image"),
                new Rect(0, 0, sender.ActualWidth, sender.ActualHeight),
                Windows.UI.Color.FromArgb(255, 160, 160, 160),
                format);
            return;
        }

        int w = (int)bitmap.SizeInPixels.Width;
        int h = (int)bitmap.SizeInPixels.Height;
        (float dpi, float x, float y) = ReferenceLayout(sender, w, h);
        var rect = new Rect(x / dpi, y / dpi, w * _refZoom / dpi, h * _refZoom / dpi);

        CanvasImageBrush checker = GetChecker(sender);
        checker.Transform = Matrix3x2.CreateTranslation((float)rect.X, (float)rect.Y);
        ds.FillRectangle(rect, checker);
        ds.DrawImage(
            bitmap,
            rect,
            new Rect(0, 0, w, h),
            1f,
            _refZoom >= 1 ? CanvasImageInterpolation.NearestNeighbor : CanvasImageInterpolation.Linear);
        ds.DrawRectangle(rect, FrameColor, 1 / dpi);
    }

    // ---- 操作 ----

    private void RefCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _refPressed = RefCanvas.CapturePointer(e.Pointer);
        _refPanning = false;
        _refPressAt = e.GetCurrentPoint(RefCanvas).Position;
        e.Handled = true;
    }

    private void RefCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_refPressed)
        {
            return;
        }

        Point now = e.GetCurrentPoint(RefCanvas).Position;
        double dx = now.X - _refPressAt.X;
        double dy = now.Y - _refPressAt.Y;
        if (!_refPanning && Math.Abs(dx) + Math.Abs(dy) < 4)
        {
            return; // 少しだけ動いたのはクリックとみなす
        }

        float dpi = RefCanvas.Dpi / 96f;
        _refPanning = true;
        _refFit = false;
        _refPanX += (float)dx * dpi;
        _refPanY += (float)dy * dpi;
        _refPressAt = now;
        RefCanvas.Invalidate();
    }

    private void RefCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_refPressed)
        {
            return;
        }

        _refPressed = false;
        RefCanvas.ReleasePointerCapture(e.Pointer);
        if (!_refPanning)
        {
            PickReferenceColor(e.GetCurrentPoint(RefCanvas).Position);
        }
    }

    private void RefCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => _refPressed = false;

    private void RefCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _refFit = true;
        RefCanvas.Invalidate();
    }

    private void RefCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_refBitmap is null)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(RefCanvas);
        bool zoomIn = point.Properties.MouseWheelDelta > 0;
        float before = _refZoom;
        float after = before >= 1 && (zoomIn || before > 1)
            ? ZoomLevels.Next((int)before, zoomIn ? 1 : -1)
            : before * (zoomIn ? 1.25f : 0.8f);
        if (!zoomIn && before <= 1)
        {
            after = Math.Max(after, 0.05f);
        }

        if (zoomIn && before < 1 && after > 1)
        {
            after = 1; // 縮小から拡大へ移るときは、いったん等倍で止める
        }

        // ポインターの下の場所が動かないように、ずれを合わせる
        float dpi = RefCanvas.Dpi / 96f;
        int w = (int)_refBitmap.SizeInPixels.Width;
        int h = (int)_refBitmap.SizeInPixels.Height;
        (_, float x, float y) = ReferenceLayout(RefCanvas, w, h);
        float px = (float)point.Position.X * dpi;
        float py = (float)point.Position.Y * dpi;
        float ix = (px - x) / before;
        float iy = (py - y) / before;
        _refFit = false;
        _refZoom = after;
        float viewW = (float)RefCanvas.ActualWidth * dpi;
        float viewH = (float)RefCanvas.ActualHeight * dpi;
        _refPanX = px - ix * after - (viewW - w * after) / 2;
        _refPanY = py - iy * after - (viewH - h * after) / 2;
        RefCanvas.Invalidate();
        e.Handled = true;
    }

    /// <summary>クリックした場所の色を描画色にする。</summary>
    private void PickReferenceColor(Point position)
    {
        if (_refBitmap is null || ReferencePixels() is not { } image)
        {
            return;
        }

        float dpi = RefCanvas.Dpi / 96f;
        (_, float x, float y) = ReferenceLayout(RefCanvas, image.Width, image.Height);
        int ix = (int)MathF.Floor(((float)position.X * dpi - x) / _refZoom);
        int iy = (int)MathF.Floor(((float)position.Y * dpi - y) / _refZoom);
        if (image.Contains(ix, iy))
        {
            SetCurrentColor(image.GetPixel(ix, iy));
        }
    }

    // ---- 左右の境目 ----

    private double _splitStartX;
    private double _splitStartRatio;
    private bool _splitDragging;

    private void RefSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _splitDragging = RefSplitter!.CapturePointer(e.Pointer);
        _splitStartX = e.GetCurrentPoint(CanvasArea).Position.X;
        _splitStartRatio = _refRatio;
        e.Handled = true;
    }

    private void RefSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_splitDragging || CanvasArea.ActualWidth <= 0)
        {
            return;
        }

        double dx = e.GetCurrentPoint(CanvasArea).Position.X - _splitStartX;
        _refRatio = Math.Clamp(_splitStartRatio - dx / CanvasArea.ActualWidth, 0.15, 0.85);
        ApplySplitRatio();
        e.Handled = true;
    }

    private void RefSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _splitDragging = false;
        RefSplitter!.ReleasePointerCapture(e.Pointer);
    }

    private void RefSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => _splitDragging = false;
}
