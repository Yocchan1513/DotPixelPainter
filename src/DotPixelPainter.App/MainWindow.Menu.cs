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
        Add(FileMenu, "新規...", "Ctrl+N", () => ErrorLog.Run("新規作成", NewDocumentWithDialogAsync));
        Add(FileMenu, "開く...", "Ctrl+O", () => ErrorLog.Run("開く", OpenAsync));
        FileMenu.Items.Add(new MenuFlyoutSeparator());
        Add(FileMenu, "保存", "Ctrl+S", () => ErrorLog.Run("保存", SaveCurrentAsync));
        Add(FileMenu, "別名で保存...", "Ctrl+Shift+S", () => ErrorLog.Run("別名で保存", SaveCurrentAsAsync));
        Add(FileMenu, "拡大して書き出し...", null, () => ErrorLog.Run("拡大して書き出し", ExportScaledAsync));
        FileMenu.Items.Add(new MenuFlyoutSeparator());
        Add(FileMenu, "タブを閉じる", "Ctrl+W", () =>
        {
            if (Tabs.SelectedItem is TabViewItem item)
            {
                ErrorLog.Run("タブを閉じる", () => CloseTabAsync(item));
            }
        });
        Add(FileMenu, "終了", null, Close);

        // 編集
        MenuFlyoutItem undo = Add(EditMenu, "元に戻す", "Ctrl+Z", Undo);
        MenuFlyoutItem redo = Add(EditMenu, "やり直し", "Ctrl+Y", Redo);
        SyncEnabled(UndoButton, undo);
        SyncEnabled(RedoButton, redo);
        EditMenu.Items.Add(new MenuFlyoutSeparator());
        Add(EditMenu, "切り取り", "Ctrl+X", () => ErrorLog.Run("切り取り", () => CopyAsync(cut: true)));
        Add(EditMenu, "コピー", "Ctrl+C", () => ErrorLog.Run("コピー", () => CopyAsync(cut: false)));
        Add(EditMenu, "貼り付け", "Ctrl+V", () => ErrorLog.Run("貼り付け", PasteAsync));

        // 画像
        Add(ImageMenu, "画像の大きさ...", null, () => ErrorLog.Run("画像の大きさ", ShowScaleDialogAsync));
        Add(ImageMenu, "キャンバスの大きさ...", null, () => ErrorLog.Run("キャンバスの大きさ", ShowCanvasSizeDialogAsync));
        Add(ImageMenu, "選択範囲で切り抜く", null, () =>
        {
            if (CurrentTab is { Selection: null })
            {
                ErrorLog.Run("切り抜き", () => ShowMessageAsync("切り抜けません", "先に、選択ツールで残したい範囲を選んでください。"));
                return;
            }

            CropToSelection();
        });
        ImageMenu.Items.Add(new MenuFlyoutSeparator());
        Add(ImageMenu, "左右反転", null, () => ImageAction(d => d.FlipImage(horizontal: true)));
        Add(ImageMenu, "上下反転", null, () => ImageAction(d => d.FlipImage(horizontal: false)));
        Add(ImageMenu, "右に90°回転", null, () => ImageAction(d => d.RotateImage(clockwise: true)));
        Add(ImageMenu, "左に90°回転", null, () => ImageAction(d => d.RotateImage(clockwise: false)));
        ImageMenu.Items.Add(new MenuFlyoutSeparator());
        Add(ImageMenu, "色の置き換え...", null, () => ErrorLog.Run("色の置き換え", ShowReplaceColorDialogAsync));
        Add(ImageMenu, "減色...", null, () => ErrorLog.Run("減色", ShowReduceColorsDialogAsync));
        Add(ImageMenu, "使っている色をパレットに取り込む...", null, () => ErrorLog.Run("パレットに取り込む", ImportUsedColorsAsync));
        ImageMenu.Items.Add(new MenuFlyoutSeparator());
        Add(ImageMenu, "線の整形（角の余分な1ドットを消す）", null, CleanupLines);

        // 選択範囲
        Add(SelectMenu, "すべて選択", "Ctrl+A", SelectAll);
        Add(SelectMenu, "選択を解除", "Ctrl+D", Deselect);
        SelectMenu.Items.Add(new MenuFlyoutSeparator());
        Add(SelectMenu, "左右反転", "Shift+H", FlipSelectionHorizontal);
        Add(SelectMenu, "上下反転", "Shift+V", FlipSelectionVertical);
        Add(SelectMenu, "右に90°回転", "Shift+R", () => RotateSelection(clockwise: true));
        Add(SelectMenu, "左に90°回転", "Shift+L", () => RotateSelection(clockwise: false));

        // レイヤー
        Add(LayerMenu, "新しいレイヤー", null, () => LayerAdd_Click(this, new RoutedEventArgs()));
        Add(LayerMenu, "レイヤーを複製", null, () => LayerDuplicate_Click(this, new RoutedEventArgs()));
        LayerMenu.Items.Add(new MenuFlyoutSeparator());
        Add(LayerMenu, "1つ上へ", null, () => LayerUp_Click(this, new RoutedEventArgs()));
        Add(LayerMenu, "1つ下へ", null, () => LayerDown_Click(this, new RoutedEventArgs()));
        LayerMenu.Items.Add(new MenuFlyoutSeparator());
        Add(LayerMenu, "下のレイヤーと結合", null, () => LayerMerge_Click(this, new RoutedEventArgs()));
        Add(LayerMenu, "レイヤーを削除", null, () => LayerDelete_Click(this, new RoutedEventArgs()));

        // 表示
        Add(ViewMenu, "拡大", null, () => ZoomAtCenter(1));
        Add(ViewMenu, "縮小", null, () => ZoomAtCenter(-1));
        ViewMenu.Items.Add(new MenuFlyoutSeparator());
        AddToggle(ViewMenu, "グリッド", GridToggle, () => RedrawAll());
        AddToggle(ViewMenu, "左右反転表示", FlipToggle, () => RedrawAll());
        AddToggle(ViewMenu, "スキンとして編集", SkinToggle, () => SkinToggle_Click(SkinToggle, new RoutedEventArgs()));
        AddToggle(ViewMenu, "右のパネル", PanelToggle, () => PanelToggle_Click(PanelToggle, new RoutedEventArgs()));
        ViewMenu.Items.Add(new MenuFlyoutSeparator());
        _transparencyMenu = CreateTransparencyMenu();
        UpdateTransparencyMenu(_transparencyMenu);
        ViewMenu.Items.Add(_transparencyMenu);

        // ヘルプ（設定もここに置く）
        _associationItem = Add(HelpMenu, "", null, () => ErrorLog.Run("関連付け", ToggleAssociationAsync));
        UpdateAssociationItem();
        _restoreItem = new ToggleMenuFlyoutItem { Text = "起動時に前回のタブを開き直す", IsChecked = _restoreSession };
        _restoreItem.Click += (_, _) => _restoreSession = _restoreItem.IsChecked;
        HelpMenu.Items.Add(_restoreItem);
        HelpMenu.Items.Add(new MenuFlyoutSeparator());
        Add(HelpMenu, "エラーの記録があるフォルダを開く", null, () =>
        {
            string folder = Path.GetDirectoryName(ErrorLog.FilePath)!;
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        });
        HelpMenu.Items.Add(new MenuFlyoutSeparator());
        Add(HelpMenu, "バージョン情報", null, () => ErrorLog.Run("バージョン情報", ShowAboutAsync));
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
                ? ".dotpix の関連付けを解除する"
                : ".dotpix をダブルクリックでこのアプリで開くようにする";
        }
    }
}
