using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DotPixelPainter;

/// <summary>
/// 画面上端のメニューバー（Photoshop のような「ファイル・編集・画像・…」）。
/// 起動を軽くするため、中身は最初の描画のあとで作る。ショートカットキーは別に登録してあるので、
/// ここではキーの表示だけを付ける。
/// </summary>
public sealed partial class MainWindow
{
    private MenuFlyoutItem? _associationItem;
    private ToggleMenuFlyoutItem? _restoreItem;
    private MenuFlyoutSubItem? _transparencyMenu;
    private bool _menusBuilt;

    private void BuildMenus()
    {
        if (_menusBuilt)
        {
            return;
        }

        _menusBuilt = true;

        // ファイル
        Add(FileMenu, T("新規...", "New..."), "Ctrl+N", () => ErrorLog.Run("新規作成", NewDocumentWithDialogAsync));
        Add(FileMenu, T("開く...", "Open..."), "Ctrl+O", () => ErrorLog.Run("開く", OpenAsync));
        FileMenu.Items.Add(new MenuFlyoutSeparator());
        Add(FileMenu, T("保存", "Save"), "Ctrl+S", () => ErrorLog.Run("保存", SaveCurrentAsync));
        Add(FileMenu, T("別名で保存...", "Save as..."), "Ctrl+Shift+S", () => ErrorLog.Run("別名で保存", SaveCurrentAsAsync));
        Add(FileMenu, T("拡大して書き出し...", "Export scaled..."), null, () => ErrorLog.Run("拡大して書き出し", ExportScaledAsync));
        FileMenu.Items.Add(new MenuFlyoutSeparator());
        Add(FileMenu, T("タブを閉じる", "Close tab"), "Ctrl+W", () =>
        {
            if (Tabs.SelectedItem is TabViewItem item)
            {
                ErrorLog.Run("タブを閉じる", () => CloseTabAsync(item));
            }
        });
        Add(FileMenu, T("終了", "Exit"), null, Close);

        // 編集
        MenuFlyoutItem undo = Add(EditMenu, T("元に戻す", "Undo"), "Ctrl+Z", Undo);
        MenuFlyoutItem redo = Add(EditMenu, T("やり直し", "Redo"), "Ctrl+Y", Redo);
        SyncEnabled(UndoButton, undo);
        SyncEnabled(RedoButton, redo);
        EditMenu.Items.Add(new MenuFlyoutSeparator());
        Add(EditMenu, T("切り取り", "Cut"), "Ctrl+X", () => ErrorLog.Run("切り取り", () => CopyAsync(cut: true)));
        Add(EditMenu, T("コピー", "Copy"), "Ctrl+C", () => ErrorLog.Run("コピー", () => CopyAsync(cut: false)));
        Add(EditMenu, T("貼り付け", "Paste"), "Ctrl+V", () => ErrorLog.Run("貼り付け", PasteAsync));

        // 画像
        Add(ImageMenu, T("画像の大きさ...", "Image size..."), null, () => ErrorLog.Run("画像の大きさ", ShowScaleDialogAsync));
        Add(ImageMenu, T("キャンバスの大きさ...", "Canvas size..."), null, () => ErrorLog.Run("キャンバスの大きさ", ShowCanvasSizeDialogAsync));
        Add(ImageMenu, T("選択範囲で切り抜く", "Crop to selection"), null, () =>
        {
            if (CurrentTab is { Selection: null })
            {
                ErrorLog.Run("切り抜き", () => ShowMessageAsync(T("切り抜けません", "Cannot crop"), T("先に、選択ツールで残したい範囲を選んでください。", "First select the area to keep with the selection tool.")));
                return;
            }

            CropToSelection();
        });
        ImageMenu.Items.Add(new MenuFlyoutSeparator());
        Add(ImageMenu, T("左右反転", "Flip horizontal"), null, () => ImageAction(d => d.FlipImage(horizontal: true)));
        Add(ImageMenu, T("上下反転", "Flip vertical"), null, () => ImageAction(d => d.FlipImage(horizontal: false)));
        Add(ImageMenu, T("右に90°回転", "Rotate 90° right"), null, () => ImageAction(d => d.RotateImage(clockwise: true)));
        Add(ImageMenu, T("左に90°回転", "Rotate 90° left"), null, () => ImageAction(d => d.RotateImage(clockwise: false)));
        ImageMenu.Items.Add(new MenuFlyoutSeparator());
        Add(ImageMenu, T("色の置き換え...", "Replace color..."), null, () => ErrorLog.Run("色の置き換え", ShowReplaceColorDialogAsync));
        Add(ImageMenu, T("減色...", "Reduce colors..."), null, () => ErrorLog.Run("減色", ShowReduceColorsDialogAsync));
        Add(ImageMenu, T("使っている色をパレットに取り込む...", "Add used colors to palette..."), null, () => ErrorLog.Run("パレットに取り込む", ImportUsedColorsAsync));
        ImageMenu.Items.Add(new MenuFlyoutSeparator());
        Add(ImageMenu, T("線の整形（角の余分な1ドットを消す）", "Clean up lines (remove extra corner pixels)"), null, CleanupLines);

        // 選択範囲
        Add(SelectMenu, T("すべて選択", "Select all"), "Ctrl+A", SelectAll);
        Add(SelectMenu, T("選択を解除", "Deselect"), "Ctrl+D", Deselect);
        SelectMenu.Items.Add(new MenuFlyoutSeparator());
        Add(SelectMenu, T("左右反転", "Flip horizontal"), "Shift+H", FlipSelectionHorizontal);
        Add(SelectMenu, T("上下反転", "Flip vertical"), "Shift+V", FlipSelectionVertical);
        Add(SelectMenu, T("右に90°回転", "Rotate 90° right"), "Shift+R", () => RotateSelection(clockwise: true));
        Add(SelectMenu, T("左に90°回転", "Rotate 90° left"), "Shift+L", () => RotateSelection(clockwise: false));

        // レイヤー
        Add(LayerMenu, T("新しいレイヤー", "New layer"), null, () => LayerAdd_Click(this, new RoutedEventArgs()));
        Add(LayerMenu, T("レイヤーを複製", "Duplicate layer"), null, () => LayerDuplicate_Click(this, new RoutedEventArgs()));
        LayerMenu.Items.Add(new MenuFlyoutSeparator());
        Add(LayerMenu, T("1つ上へ", "Move up"), null, () => LayerUp_Click(this, new RoutedEventArgs()));
        Add(LayerMenu, T("1つ下へ", "Move down"), null, () => LayerDown_Click(this, new RoutedEventArgs()));
        LayerMenu.Items.Add(new MenuFlyoutSeparator());
        Add(LayerMenu, T("下のレイヤーと結合", "Merge down"), null, () => LayerMerge_Click(this, new RoutedEventArgs()));
        Add(LayerMenu, T("レイヤーを削除", "Delete layer"), null, () => LayerDelete_Click(this, new RoutedEventArgs()));

        // 表示
        Add(ViewMenu, T("拡大", "Zoom in"), null, () => ZoomAtCenter(1));
        Add(ViewMenu, T("縮小", "Zoom out"), null, () => ZoomAtCenter(-1));
        ViewMenu.Items.Add(new MenuFlyoutSeparator());
        AddToggle(ViewMenu, T("グリッド", "Grid"), GridToggle, () => RedrawAll());
        AddToggle(ViewMenu, T("左右反転表示", "Mirror view"), FlipToggle, () => RedrawAll());
        AddToggle(ViewMenu, T("スキンとして編集", "Edit as skin"), SkinToggle, () => SkinToggle_Click(SkinToggle, new RoutedEventArgs()));
        AddToggle(ViewMenu, T("右のパネル", "Right panel"), PanelToggle, () => PanelToggle_Click(PanelToggle, new RoutedEventArgs()));
        AddToggle(ViewMenu, T("画面を分割（資料を表示）", "Split view (show a reference)"), SplitToggle, () => SetSplit(SplitToggle.IsChecked == true));
        ViewMenu.Items.Add(new MenuFlyoutSeparator());
        _transparencyMenu = CreateTransparencyMenu();
        UpdateTransparencyMenu(_transparencyMenu);
        ViewMenu.Items.Add(_transparencyMenu);

        // ヘルプ（設定もここに置く）
        _associationItem = Add(HelpMenu, "", null, () => ErrorLog.Run("関連付け", ToggleAssociationAsync));
        UpdateAssociationItem();
        _restoreItem = new ToggleMenuFlyoutItem { Text = T("起動時に前回のタブを開き直す", "Reopen last tabs at startup"), IsChecked = _restoreSession };
        _restoreItem.Click += (_, _) => _restoreSession = _restoreItem.IsChecked;
        HelpMenu.Items.Add(_restoreItem);
        HelpMenu.Items.Add(CreateLanguageMenu());
        HelpMenu.Items.Add(new MenuFlyoutSeparator());
        Add(HelpMenu, T("エラーの記録があるフォルダを開く", "Open the error log folder"), null, () =>
        {
            string folder = Path.GetDirectoryName(ErrorLog.FilePath)!;
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        });
        HelpMenu.Items.Add(new MenuFlyoutSeparator());
        Add(HelpMenu, T("バージョン情報", "About"), null, () => ErrorLog.Run("バージョン情報", ShowAboutAsync));
    }

    private static MenuFlyoutItem Add(MenuBarItem menu, string text, string? keys, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        if (keys is not null)
        {
            item.KeyboardAcceleratorTextOverride = keys;
        }

        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    /// <summary>ツールバーの切り替えボタンと同じ働きの、印付きの項目。どちらで切り替えても両方の印がそろう。</summary>
    private static void AddToggle(MenuBarItem menu, string text, ToggleButton button, Action changed)
    {
        var item = new ToggleMenuFlyoutItem { Text = text, IsChecked = button.IsChecked == true, IsEnabled = button.IsEnabled };
        item.Click += (_, _) =>
        {
            button.IsChecked = item.IsChecked;
            changed();
        };
        button.Checked += (_, _) => item.IsChecked = true;
        button.Unchecked += (_, _) => item.IsChecked = false;
        button.IsEnabledChanged += (_, _) => item.IsEnabled = button.IsEnabled;
        menu.Items.Add(item);
    }

    private static void SyncEnabled(Control button, MenuFlyoutItem item)
    {
        item.IsEnabled = button.IsEnabled;
        button.IsEnabledChanged += (_, _) => item.IsEnabled = button.IsEnabled;
    }

    private void UpdateAssociationItem()
    {
        if (_associationItem is not null)
        {
            _associationItem.Text = FileAssociation.IsRegistered()
                ? T(".dotpix の関連付けを解除する", "Remove the .dotpix file association")
                : T(".dotpix をこのアプリで開くようにする（サムネイルも表示）", "Open .dotpix files with this app (and show thumbnails)");
        }
    }
}
