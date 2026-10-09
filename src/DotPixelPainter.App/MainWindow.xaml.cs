using System.Reflection;
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
    /// <summary>「v0.1.0」の形の版。csproj の Version から取る（ビルド時に付く「+コミット番号」は外す）。</summary>
    private static string AppVersion =>
        "v" + (typeof(MainWindow).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "?");

    private static readonly Color FrameColor = Color.FromArgb(255, 96, 96, 96);

    private readonly StartupProbe _probe;
    private readonly Dictionary<TabViewItem, DocumentTab> _tabs = [];

    private CanvasBitmap? _bitmap;
    private DocumentTab? _bitmapOwner;
    private CanvasImageBrush? _checker;
    private CanvasImageBrush? _lightChecker;
    private byte[] _pixelBuffer = [];
    private int _untitledCount;
    private bool _firstFrameReported;
    private bool _forceClose;

    /// <summary>起動時に渡されたファイルを開き終わるまでの処理（テスト用の操作は、これを待ってから行う）。</summary>
    private Task _startupOpen = Task.CompletedTask;

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
        _probe.Mark("xaml");

        // タブはタイトルバーの下に並べる（タイトルバーに入れる設定は起動を 150ms ほど遅くするため使わない）
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        AppWindow.Closing += AppWindow_Closing;
        SetWindowIcon(AppWindow);

        // 標準のタイトルバーの明暗を、アプリのテーマ（＝Windows の設定）に合わせる
        ApplyTitleBarTheme(Application.Current.RequestedTheme == ApplicationTheme.Dark);
        Root.ActualThemeChanged += (s, _) => ApplyTitleBarTheme(s.ActualTheme == ElementTheme.Dark);

        VersionText.Text = AppVersion;
        InitializePalette();
        SetBackColor(_backColor);
        _probe.Mark("palette");
        BuildToolPanel();
        _probe.Mark("tools");
        AddKeyboardShortcuts();
        // 終了時に開いていたファイルを覚える（計測やテストで起動したときは、本来の記録を上書きしない）
        Closed += (_, _) =>
        {
            if (ShouldRestoreSession() || _probe.FilesToOpen.Count > 0)
            {
                SaveSession();
            }
        };
        InitializePanels();
        AddTab(CreateUntitled());
        _probe.Mark("tab");
        Root.Loaded += (_, _) => _probe.Mark("loaded");

        // 倍率を手で変えていないタブは、キャンバスの大きさが変わったら倍率を選び直す
        Canvas.SizeChanged += (_, _) =>
        {
            foreach (DocumentTab tab in _tabs.Values)
            {
                if (tab.AutoZoom)
                {
                    tab.Zoom = 0;
                }
            }
        };
    }

    private enum DragMode
    {
        None,
        Paint,
        Pan,
        Shape,
        SelectRect,
        MoveSelection,
    }

    private DocumentTab? CurrentTab =>
        Tabs.SelectedItem is TabViewItem item && _tabs.TryGetValue(item, out var tab) ? tab : null;

    private bool IsFlipped => FlipToggle.IsChecked == true;

    // ---- タブ ----

    /// <summary>前回「新規作成」で選んだサイズと背景で、新しいドキュメントを作る。</summary>
    private PixelDocument CreateUntitled()
    {
        NewDocumentSettings s = NewDocumentSettings.Load();
        return CreateUntitled(s.Width, s.Height, s.WhiteBackground);
    }

    private PixelDocument CreateUntitled(int width, int height, bool whiteBackground)
    {
        _untitledCount++;
        var document = new PixelDocument($"無題 {_untitledCount}", width, height);
        if (whiteBackground)
        {
            document.ActiveLayer.Image.Fill(0xFFFFFFFF);
        }

        return document;
    }

    private void AddTab(PixelDocument document)
    {
        var tab = new DocumentTab(document);
        var item = new TabViewItem { Header = tab.Header };
        _tabs[item] = tab;
        Tabs.TabItems.Add(item);
        Tabs.SelectedItem = item;
    }

    /// <summary>このファイルを開いているタブ。保存先が同じか、保存前の PSD などの読み込み元が同じものを探す。</summary>
    private TabViewItem? FindOpenTab(string path)
    {
        string full = Path.GetFullPath(path);
        foreach (var (item, tab) in _tabs)
        {
            string? opened = tab.Document.FilePath ?? tab.ImportedFrom;
            if (opened is not null && string.Equals(Path.GetFullPath(opened), full, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
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

        UpdateWindowTitle();
    }

    private static readonly string IconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DotPixelPainter.ico");

    /// <summary>タイトルバーとタスクバーのアイコン。見つからなくても動きは続ける。</summary>
    private static void SetWindowIcon(AppWindow window)
    {
        if (File.Exists(IconPath))
        {
            window.SetIcon(IconPath);
        }
    }

    private void ApplyTitleBarTheme(bool dark) =>
        NativeMethods.SetDarkTitleBar(WindowNative.GetWindowHandle(this), dark);

    /// <summary>タイトルバーに「ファイル名 - DotPixelPainter」を出す。未保存なら名前の後ろに ● を付ける。</summary>
    private void UpdateWindowTitle()
    {
        Title = CurrentTab is { } tab ? $"{tab.Header} - DotPixelPainter" : "DotPixelPainter";
    }

    private async Task CloseTabAsync(TabViewItem item)
    {
        DocumentTab tab = _tabs[item];
        CommitFloating(tab);
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
        CommitAllFloating();
        EndStroke();
        RedrawAll();
        UpdateUndoButtons();
        UpdateWindowTitle();
        RefreshLayerList();
        UpdateSkinUi();
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
        AddShortcut(VirtualKey.A, VirtualKeyModifiers.Control, SelectAll);
        AddShortcut(VirtualKey.D, VirtualKeyModifiers.Control, Deselect);
        AddShortcut(VirtualKey.C, VirtualKeyModifiers.Control, () => ErrorLog.Run("コピー", () => CopyAsync(cut: false)));
        AddShortcut(VirtualKey.X, VirtualKeyModifiers.Control, () => ErrorLog.Run("切り取り", () => CopyAsync(cut: true)));
        AddShortcut(VirtualKey.V, VirtualKeyModifiers.Control, () => ErrorLog.Run("貼り付け", PasteAsync));
        AddShortcut(VirtualKey.H, VirtualKeyModifiers.Shift, FlipSelectionHorizontal);
        AddShortcut(VirtualKey.V, VirtualKeyModifiers.Shift, FlipSelectionVertical);
        AddShortcut(VirtualKey.R, VirtualKeyModifiers.Shift, () => RotateSelection(clockwise: true));
        AddShortcut(VirtualKey.L, VirtualKeyModifiers.Shift, () => RotateSelection(clockwise: false));
        AddShortcut(VirtualKey.N, VirtualKeyModifiers.Control, () => ErrorLog.Run("新規作成", NewDocumentWithDialogAsync));
        AddShortcut(VirtualKey.O, VirtualKeyModifiers.Control, () => ErrorLog.Run("開く", OpenAsync));
        AddShortcut(VirtualKey.S, VirtualKeyModifiers.Control, () => ErrorLog.Run("保存", SaveCurrentAsync));
        AddShortcut(VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => ErrorLog.Run("別名で保存", SaveCurrentAsAsync));
        AddShortcut(VirtualKey.W, VirtualKeyModifiers.Control, () =>
        {
            if (Tabs.SelectedItem is TabViewItem item)
            {
                ErrorLog.Run("タブを閉じる", () => CloseTabAsync(item));
            }
        });
        AddShortcut(VirtualKey.Tab, VirtualKeyModifiers.Control, () => SelectRelativeTab(1));
        AddShortcut(VirtualKey.Tab, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => SelectRelativeTab(-1));
    }

    private readonly List<(VirtualKey Key, VirtualKeyModifiers Modifiers, Action Action)> _shortcuts = [];

    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        _shortcuts.Add((key, modifiers, action));
        AttachShortcut(Root, key, modifiers, action);
    }

    /// <summary>切り離したパネルのウィンドウでも、同じショートカットキーが効くようにする。</summary>
    private void AttachShortcuts(UIElement target)
    {
        foreach (var (key, modifiers, action) in _shortcuts)
        {
            AttachShortcut(target, key, modifiers, action);
        }

        target.KeyDown += Root_KeyDown;
    }

    private static void AttachShortcut(UIElement target, VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) =>
        {
            // 文字入力欄では Ctrl+A（全選択）や Ctrl+Z（入力の取り消し）を入力欄に任せる
            if (target.XamlRoot is not null && FocusManager.GetFocusedElement(target.XamlRoot) is TextBox)
            {
                return;
            }

            e.Handled = true;
            action();
        };
        target.KeyboardAccelerators.Add(accelerator);
    }

    private void New_Click(object sender, RoutedEventArgs e) => ErrorLog.Run("新規作成", NewDocumentWithDialogAsync);

    private void Open_Click(object sender, RoutedEventArgs e) => ErrorLog.Run("開く", OpenAsync);

    private void Save_Click(object sender, RoutedEventArgs e) => ErrorLog.Run("保存", SaveCurrentAsync);

    private void SaveAs_Click(object sender, RoutedEventArgs e) => ErrorLog.Run("別名で保存", SaveCurrentAsAsync);

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomAtCenter(1);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomAtCenter(-1);

    private void ViewOption_Click(object sender, RoutedEventArgs e) => RedrawAll();

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    // ---- 元に戻す／やり直し ----

    private void Undo()
    {
        CommitFloating(CurrentTab); // 持ち上げ中の移動を確定してから、それを戻す
        EndStroke();
        if (CurrentTab is { } tab && tab.Document.History.Undo())
        {
            AfterHistoryChange(tab);
        }
    }

    private void Redo()
    {
        CommitFloating(CurrentTab);
        EndStroke();
        if (CurrentTab is { } tab && tab.Document.History.Redo())
        {
            AfterHistoryChange(tab);
        }
    }

    private void AfterHistoryChange(DocumentTab tab)
    {
        tab.ImageChanged = true;
        if (tab.ShownSize != (tab.Document.Width, tab.Document.Height))
        {
            // 画像の大きさが変わった: 選択を外し、画面に収まる倍率で真ん中に出し直す
            tab.ShownSize = (tab.Document.Width, tab.Document.Height);
            tab.Selection = null;
            tab.Zoom = 0;
            tab.AutoZoom = true;
            tab.PanX = 0;
            tab.PanY = 0;
            UpdateSkinUi();
        }

        RefreshHeader(tab);
        UpdateUndoButtons();
        RefreshLayerList(); // 元に戻すでレイヤーが増減・入れ替わることがある
        UpdateStatus(null);
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
        picker.FileTypeFilter.Add(DotPixFile.Extension);
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(PsdReader.Extension);
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            await OpenDocumentAsync(file);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("開けませんでした", ex.Message);
        }
    }

    /// <summary>ファイルを開いてタブに追加する。PSD で扱いを変えた部分があれば、開いたあとに知らせる。</summary>
    private async Task OpenDocumentAsync(StorageFile file)
    {
        // すでに開いているファイルなら、読み直さずにそのタブへ切り替える（描きかけの内容を残すため）
        if (FindOpenTab(file.Path) is { } open)
        {
            Tabs.SelectedItem = open;
            return;
        }

        (PixelDocument document, IReadOnlyList<string> warnings) = await LoadDocumentAsync(file);
        AddTab(document);
        if (document.FilePath is null)
        {
            _tabs[(TabViewItem)Tabs.SelectedItem].ImportedFrom = file.Path;
        }

        if (warnings.Count > 0)
        {
            await ShowMessageAsync("読み込みについて", string.Join("\n\n", warnings));
        }
    }

    /// <summary>起動時に渡されたファイルを開く。何も描いていない最初の「無題」タブは閉じる。</summary>
    private Task OpenStartupFilesAsync() => OpenFilesReplacingBlankAsync(_probe.FilesToOpen, reportErrors: true);

    /// <summary>
    /// ファイルを順に開く。1つでも開けたら、何も描いていない最初の「無題」タブは閉じる。
    /// reportErrors が false なら、開けなかったファイルは黙って飛ばす（前回のタブの復元で、消えたファイルなど）。
    /// </summary>
    private async Task<int> OpenFilesReplacingBlankAsync(IEnumerable<string> paths, bool reportErrors)
    {
        TabViewItem? blank = Tabs.TabItems.Count == 1
            && Tabs.TabItems[0] is TabViewItem only
            && _tabs[only].Document.FilePath is null
            && !_tabs[only].Document.History.CanUndo ? only : null;

        int opened = 0;
        foreach (string path in paths)
        {
            try
            {
                await OpenDocumentAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
                opened++;
            }
            catch (Exception ex)
            {
                if (reportErrors)
                {
                    await ShowMessageAsync("開けませんでした", $"{path}\n{ex.Message}");
                }
            }
        }

        if (opened > 0 && blank is not null)
        {
            Tabs.TabItems.Remove(blank);
            _tabs.Remove(blank);
        }

        return opened;
    }

    private static bool IsDotPix(string path) =>
        string.Equals(Path.GetExtension(path), DotPixFile.Extension, StringComparison.OrdinalIgnoreCase);

    private static async Task<(PixelDocument Document, IReadOnlyList<string> Warnings)> LoadDocumentAsync(StorageFile file)
    {
        string extension = Path.GetExtension(file.Path);

        // インストール不要のアプリなので、ファイルは普通にパスで読める（重い処理は裏で）
        if (string.Equals(extension, PsdReader.Extension, StringComparison.OrdinalIgnoreCase))
        {
            PsdImportResult result = await Task.Run(() => PsdReader.Read(File.ReadAllBytes(file.Path), file.Name));
            // PSD には書き出せないので、保存先は保存するときに選んでもらう（FilePath は空のまま）
            result.Document.Name = Path.ChangeExtension(file.Name, DotPixFile.Extension);
            return (result.Document, result.Warnings);
        }

        if (IsDotPix(file.Path))
        {
            PixelDocument document = await Task.Run(() =>
            {
                using FileStream stream = File.OpenRead(file.Path);
                return DotPixFile.Read(stream, file.Name);
            });
            document.FilePath = file.Path;
            return (document, []);
        }

        return (await PngFile.LoadAsync(file), []);
    }

    private async Task SaveCurrentAsync()
    {
        if (CurrentTab is { } tab)
        {
            await SaveAsync(tab);
        }
    }

    private async Task SaveCurrentAsAsync()
    {
        if (CurrentTab is { } tab)
        {
            await SaveAsync(tab, saveAs: true);
        }
    }

    /// <summary>
    /// 保存する。.dotpix ならレイヤーごと、.png なら表示どおりに1枚にまとめて保存する。
    /// まだ保存先がないとき、または saveAs のときは保存先を選んでもらう（.dotpix が先頭の候補）。
    /// </summary>
    private async Task<bool> SaveAsync(DocumentTab tab, bool saveAs = false)
    {
        CommitFloating(tab);
        PixelDocument document = tab.Document;
        StorageFile? file = null;
        try
        {
            if (!saveAs && document.FilePath is not null)
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
            picker.FileTypeChoices.Add("DotPixelPainter（レイヤーを残す）", [DotPixFile.Extension]);
            picker.FileTypeChoices.Add("PNG 画像（1枚にまとめる）", [".png"]);
            file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return false;
            }

            if (!IsDotPix(file.Path) && document.Layers.Count > 1 && !await ConfirmFlattenAsync())
            {
                return false;
            }
        }

        try
        {
            if (IsDotPix(file.Path))
            {
                string path = file.Path;
                await Task.Run(() =>
                {
                    // 途中で失敗しても元のファイルを壊さないよう、一時ファイルに書いてから置き換える
                    string temp = path + ".saving";
                    using (FileStream stream = File.Create(temp))
                    {
                        DotPixFile.Write(document, stream);
                    }

                    File.Move(temp, path, overwrite: true);
                });
            }
            else
            {
                await PngFile.SaveAsync(document, file);
            }
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

    /// <summary>レイヤーが複数あるときに PNG で保存しようとしたら、1枚にまとまることを確かめる。</summary>
    private async Task<bool> ConfirmFlattenAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "PNG ではレイヤーが1枚にまとまります",
            Content = "表示中のレイヤーを重ねた見た目で保存します。レイヤーを残したいときは .dotpix で保存してください。\n（アプリの中のレイヤーはそのまま残ります）",
            PrimaryButtonText = "PNG で保存",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
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

        PixelImage composite = document.Composite(tab.Floating);
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

    /// <summary>透明部分の下地（「表示」メニューの「透明部分の表示」で選んだ色）。</summary>
    private CanvasImageBrush GetChecker(ICanvasResourceCreator resourceCreator)
    {
        if (_checker is not null && _checker.Device == resourceCreator.Device)
        {
            return _checker;
        }

        EnsureTransparencyLoaded();
        _checker?.Dispose();
        _checker = CreateChecker(resourceCreator, _transparentA, _transparentB);
        return _checker;
    }

    /// <summary>設定によらない明るい市松模様。不透明度のバーなど、色そのものを見せる場所に使う。</summary>
    private CanvasImageBrush GetLightChecker(ICanvasResourceCreator resourceCreator)
    {
        if (_lightChecker is null || _lightChecker.Device != resourceCreator.Device)
        {
            _lightChecker?.Dispose();
            _lightChecker = CreateChecker(resourceCreator, 0xFFFFFFFF, 0xFFE8E8E8);
        }

        return _lightChecker;
    }

    private static CanvasImageBrush CreateChecker(ICanvasResourceCreator resourceCreator, uint a, uint b)
    {
        var tile = new CanvasRenderTarget(resourceCreator, 16, 16, 96f);
        using (CanvasDrawingSession ds = tile.CreateDrawingSession())
        {
            ds.Clear(ToColor(a));
            ds.FillRectangle(8, 0, 8, 8, ToColor(b));
            ds.FillRectangle(0, 8, 8, 8, ToColor(b));
        }

        return new CanvasImageBrush(resourceCreator, tile)
        {
            ExtendX = CanvasEdgeBehavior.Wrap,
            ExtendY = CanvasEdgeBehavior.Wrap,
            Interpolation = CanvasImageInterpolation.NearestNeighbor,
        };
    }

    /// <summary>画像左上の位置（物理ピクセル）。整数に揃えてドットがにじまないようにする。</summary>
    private static (float Scale, float OriginX, float OriginY) Layout(CanvasControl canvas, DocumentTab tab)
    {
        float scale = canvas.Dpi / 96f;
        float viewW = (float)canvas.ActualWidth * scale;
        float viewH = (float)canvas.ActualHeight * scale;
        if (tab.Zoom == 0 && viewW > 0 && viewH > 0)
        {
            // 新しく開いたタブは、画面に収まるいちばん大きい倍率で表示する
            tab.Zoom = ZoomLevels.Fit(tab.Document.Width, tab.Document.Height, viewW, viewH);
        }

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
        bool zoomWasUnset = tab.Zoom == 0;
        (float scale, float ox, float oy) = Layout(sender, tab);
        if (tab.Zoom == 0)
        {
            return; // まだ大きさが決まっていない
        }

        if (zoomWasUnset)
        {
            // 倍率が決まったので表示を更新する（描画中は文字を変えず、終わってから）
            DispatcherQueue.TryEnqueue(() => UpdateStatus(null));
        }

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
            DrawPixelGrid(ds, rect, document, GridColor, tab.Zoom / scale, 1 / scale, (float)sender.ActualWidth, (float)sender.ActualHeight);
        }

        ds.DrawRectangle(rect, FrameColor, 1 / scale);
        DrawSkinGuide(ds, tab, rect, tab.Zoom / scale, 1 / scale);
        DrawSelectionFrame(ds, tab, rect, tab.Zoom / scale, 1 / scale);

        if (!_firstFrameReported)
        {
            _firstFrameReported = true;
            _probe.Mark("first-draw");
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_probe.ReportFirstFrame())
                {
                    _forceClose = true;
                    Close();
                    return;
                }

                RestorePanelLayout();

                _restoreSession = LoadSession().Restore;
                BuildMenus();
                if (_probe.FilesToOpen.Count > 0)
                {
                    _startupOpen = OpenStartupFilesAsync();
                    ErrorLog.Run("起動時のファイルを開く", () => _startupOpen);
                }
                else if (ShouldRestoreSession())
                {
                    ErrorLog.Run("前回のタブを開き直す", RestoreSessionAsync);
                }

                if (_probe.TestOpen == "tile")
                {
                    TileToggle.IsChecked = true;
                    TileToggle_Click(TileToggle, new RoutedEventArgs());
                }

                if (_probe.TestOpen == "new-document")
                {
                    ErrorLog.Run("新規作成（テスト）", NewDocumentWithDialogAsync);
                }
                else if (_probe.TestOpen == "about")
                {
                    ErrorLog.Run("バージョン情報（テスト）", ShowAboutAsync);
                }
                else if (_probe.TestOpen == "scale")
                {
                    ErrorLog.Run("画像の大きさ（テスト）", async () => { await _startupOpen; await ShowScaleDialogAsync(); });
                }
                else if (_probe.TestOpen == "canvas-size")
                {
                    ErrorLog.Run("キャンバスの大きさ（テスト）", async () => { await _startupOpen; await ShowCanvasSizeDialogAsync(); });
                }
                else if (_probe.TestOpen == "export")
                {
                    ErrorLog.Run("拡大して書き出し（テスト）", async () => { await _startupOpen; await ExportScaledAsync(); });
                }
                else if (_probe.TestOpen == "rotate")
                {
                    ErrorLog.Run("回転（テスト）", async () =>
                    {
                        await _startupOpen;
                        ImageAction(d => d.RotateImage(clockwise: true));
                    });
                }
                else if (_probe.TestOpen?.StartsWith("detach:", StringComparison.Ordinal) == true
                    && _panels.TryGetValue(_probe.TestOpen["detach:".Length..], out DockPanel? panel)
                    && panel.Floating is null)
                {
                    Detach(panel, null);
                }
            });
        }
    }

    /// <summary>1pxグリッド線。画面に見えている範囲の線だけを、物理1ピクセル幅で描く。</summary>
    private static void DrawPixelGrid(CanvasDrawingSession ds, Rect r, PixelDocument document, Color gridColor, float cell, float stroke, float viewW, float viewH)
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
            ds.DrawLine(px, Math.Max(top, 0), px, Math.Min(bottom, viewH), gridColor, stroke);
        }

        int y0 = Math.Max(1, (int)MathF.Ceiling(-top / cell));
        int y1 = Math.Min(document.Height - 1, (int)MathF.Floor((viewH - top) / cell));
        for (int y = y0; y <= y1; y++)
        {
            float py = top + y * cell + half;
            ds.DrawLine(Math.Max(left, 0), py, Math.Min(right, viewW), py, gridColor, stroke);
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
        if (IsSkinTab(tab) && SkinLayout.IsSkinSize(document.Width, document.Height))
        {
            if (PreviewBackgroundColor() is { } skinBackground)
            {
                ds.Clear(skinBackground);
            }

            DrawSkin3D(sender, ds, bitmap, document);
            return;
        }

        if (TileToggle.IsChecked == true)
        {
            DrawTiledPreview(sender, ds, bitmap, document);
            return;
        }

        CanvasImageBrush checker = GetChecker(sender);
        float scale = sender.Dpi / 96f;
        float availW = (float)sender.ActualWidth;
        float availH = (float)sender.ActualHeight;
        float y = 0;

        foreach (int times in (ReadOnlySpan<int>)[1, 2])
        {
            float w = document.Width * times / scale;
            float h = document.Height * times / scale;
            bool tooBig = times == 1 && (w > availW || h > availH);
            if (tooBig)
            {
                // 等倍でも入りきらない大きな絵は、1枚だけを枠いっぱいに縮めて見せる
                float fit = Math.Min(availW / w, availH / h);
                w *= fit;
                h *= fit;
            }
            else if (w > availW)
            {
                h *= availW / w;
                w = availW;
            }

            var rect = new Rect(0, y, w, h);
            if (PreviewBackgroundColor() is { } background)
            {
                ds.FillRectangle(rect, background);
            }
            else
            {
                checker.Transform = Matrix3x2.CreateTranslation(0, y);
                ds.FillRectangle(rect, checker);
            }

            if (IsFlipped)
            {
                ds.Transform = Matrix3x2.CreateScale(-1, 1, new Vector2(w / 2, 0));
            }

            ds.DrawImage(bitmap, rect, new Rect(0, 0, document.Width, document.Height), 1f, CanvasImageInterpolation.NearestNeighbor);
            ds.Transform = Matrix3x2.Identity;
            if (tooBig)
            {
                break;
            }

            y = MathF.Ceiling((y + h) * scale + 16) / scale;
        }
    }

    // ---- 入力 ----

    private (int X, int Y) ToImage(DocumentTab tab, Point p)
    {
        (float scale, float ox, float oy) = Layout(Canvas, tab);
        if (tab.Zoom == 0)
        {
            return (-1, -1); // まだ表示されていない
        }

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
            tab.AutoZoom = false;
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
        else if (props.IsRightButtonPressed && _tool == Tool.Select)
        {
            // 選択ツールの右クリックは、コピー・反転などのメニュー
            ShowSelectionMenu(point.Position);
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
        UpdateSkinHover(tab, (x, y));
        UpdateStatus((x, y));

        if (_drag == DragMode.Paint)
        {
            if (x != _lastX || y != _lastY)
            {
                Paint(tab, PixelLine.Enumerate(_lastX, _lastY, x, y));
                (_lastX, _lastY) = (x, y);
            }
        }
        else if (_drag is DragMode.SelectRect or DragMode.MoveSelection)
        {
            UpdateSelectDrag(tab, (x, y));
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
        if (_drag is DragMode.SelectRect or DragMode.MoveSelection && CurrentTab is { } selectTab)
        {
            EndSelectDrag(selectTab, ToImage(selectTab, e.GetCurrentPoint(Canvas).Position));
        }

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
        foreach ((int x, int y) in _brush.Stamp(points))
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
        if (newZoom == tab.Zoom || tab.Zoom == 0)
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
        tab.AutoZoom = false;
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
        string skin = IsSkinTab(tab) && SkinStatus() is { Length: > 0 } part ? $"　｜　{part}" : "";
        StatusText.Text = $"{tab.Document.Width} × {tab.Document.Height}　｜　倍率 {tab.Zoom * 100}%　｜　座標 {pos}{skin}　｜　{ToolStatus()}{MaskStatus()}　｜　{tab.Document.ActiveLayer.Name}";
    }

    private static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
