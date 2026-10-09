using DotPixelPainter.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotPixelPainter;

/// <summary>
/// 「画像」メニュー: 画像の拡大縮小、キャンバスの大きさ、切り抜き、全体の反転・回転。
/// 「ファイル」メニューの「拡大して書き出し」もここ。どれも色を混ぜない（ドットのまま）。
/// </summary>
public sealed partial class MainWindow
{
    private static readonly int[] ExportScales = [1, 2, 3, 4, 6, 8, 10, 16];

    /// <summary>画像全体の操作を行い、表示を合わせる。持ち上げ中の中身は先に置く。</summary>
    private void ImageAction(Func<PixelDocument, bool> action)
    {
        CommitFloating(CurrentTab);
        EndStroke();
        if (CurrentTab is { } tab && action(tab.Document))
        {
            AfterHistoryChange(tab);
        }
    }

    private void CropToSelection()
    {
        if (CurrentTab is { Selection: { } selection })
        {
            ImageAction(d => d.Crop(selection));
        }
    }

    // ---- 画像の大きさ（拡大・縮小） ----

    private async Task ShowScaleDialogAsync()
    {
        if (CurrentTab is not { } tab)
        {
            return;
        }

        int oldW = tab.Document.Width;
        int oldH = tab.Document.Height;
        NumberBox widthBox = SizeBox("幅（px）", oldW);
        NumberBox heightBox = SizeBox("高さ（px）", oldH);
        var keepRatio = new CheckBox { Content = "縦横の比率を保つ", IsChecked = true };

        // 比率を保つときは、片方を変えたらもう片方を合わせる（合わせている間の通知は無視する）
        bool syncing = false;
        widthBox.ValueChanged += (_, _) =>
        {
            if (syncing || keepRatio.IsChecked != true || double.IsNaN(widthBox.Value))
            {
                return;
            }

            syncing = true;
            heightBox.Value = Math.Max(1, Math.Round(widthBox.Value * oldH / oldW));
            syncing = false;
        };
        heightBox.ValueChanged += (_, _) =>
        {
            if (syncing || keepRatio.IsChecked != true || double.IsNaN(heightBox.Value))
            {
                return;
            }

            syncing = true;
            widthBox.Value = Math.Max(1, Math.Round(heightBox.Value * oldW / oldH));
            syncing = false;
        };

        var quick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach ((string label, double factor) in new[] { ("½", 0.5), ("×2", 2.0), ("×3", 3.0), ("×4", 4.0) })
        {
            var button = new Button { Content = label, MinWidth = 52 };
            button.Click += (_, _) =>
            {
                syncing = true;
                widthBox.Value = Math.Max(1, Math.Round(oldW * factor));
                heightBox.Value = Math.Max(1, Math.Round(oldH * factor));
                syncing = false;
            };
            quick.Children.Add(button);
        }

        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sizeRow.Children.Add(widthBox);
        sizeRow.Children.Add(heightBox);

        var panel = new StackPanel { Spacing = 12, MinWidth = 320 };
        panel.Children.Add(new TextBlock { Text = $"今の大きさ: {oldW} × {oldH}", FontSize = 12 });
        panel.Children.Add(sizeRow);
        panel.Children.Add(keepRatio);
        panel.Children.Add(quick);
        panel.Children.Add(new TextBlock
        {
            Text = "色を混ぜずに拡大・縮小します（ドットはにじみません）。整数倍の拡大なら、1ドットがそのまま大きな四角になります。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        if (await ShowSizeDialogAsync("画像の大きさを変える", panel, widthBox, heightBox, "変更") is { } size)
        {
            ImageAction(d => d.ScaleImage(size.Width, size.Height));
        }
    }

    // ---- キャンバスの大きさ（絵の大きさはそのままで、周りを広げる・切る） ----

    private async Task ShowCanvasSizeDialogAsync()
    {
        if (CurrentTab is not { } tab)
        {
            return;
        }

        int oldW = tab.Document.Width;
        int oldH = tab.Document.Height;
        NumberBox widthBox = SizeBox("幅（px）", oldW);
        NumberBox heightBox = SizeBox("高さ（px）", oldH);

        // 元の絵をどこに寄せるか（3×3 のボタン。初めは真ん中）
        int anchorX = 1;
        int anchorY = 1;
        var anchorGrid = new Grid { RowSpacing = 4, ColumnSpacing = 4, HorizontalAlignment = HorizontalAlignment.Left };
        var anchors = new List<(ToggleButton Button, int X, int Y)>();
        for (int i = 0; i < 3; i++)
        {
            anchorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            anchorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        void UpdateAnchors()
        {
            foreach (var (button, x, y) in anchors)
            {
                button.IsChecked = x == anchorX && y == anchorY;
            }
        }

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                var button = new ToggleButton { Width = 36, Height = 36, Padding = new Thickness(0) };
                int ax = x;
                int ay = y;
                button.Click += (_, _) =>
                {
                    (anchorX, anchorY) = (ax, ay);
                    UpdateAnchors();
                };
                Grid.SetColumn(button, x);
                Grid.SetRow(button, y);
                anchorGrid.Children.Add(button);
                anchors.Add((button, x, y));
            }
        }

        UpdateAnchors();

        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sizeRow.Children.Add(widthBox);
        sizeRow.Children.Add(heightBox);

        var panel = new StackPanel { Spacing = 12, MinWidth = 320 };
        panel.Children.Add(new TextBlock { Text = $"今の大きさ: {oldW} × {oldH}", FontSize = 12 });
        panel.Children.Add(sizeRow);
        panel.Children.Add(new TextBlock { Text = "今の絵を置く位置", FontSize = 12 });
        panel.Children.Add(anchorGrid);
        panel.Children.Add(new TextBlock
        {
            Text = "絵の大きさは変えずに、周りを透明で広げたり、はみ出す部分を切ったりします。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        if (await ShowSizeDialogAsync("キャンバスの大きさを変える", panel, widthBox, heightBox, "変更") is { } size)
        {
            // 寄せる位置: 0 = 左（上）、1 = 真ん中、2 = 右（下）
            int offsetX = (size.Width - oldW) * anchorX / 2;
            int offsetY = (size.Height - oldH) * anchorY / 2;
            ImageAction(d => d.ResizeCanvas(size.Width, size.Height, offsetX, offsetY));
        }
    }

    /// <summary>幅と高さを入れるダイアログを出し、決定されたら大きさを返す。</summary>
    private async Task<(int Width, int Height)?> ShowSizeDialogAsync(string title, UIElement content, NumberBox widthBox, NumberBox heightBox, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
        };

        void Validate() => dialog.IsPrimaryButtonEnabled = IsValid(widthBox.Value) && IsValid(heightBox.Value);
        widthBox.ValueChanged += (_, _) => Validate();
        heightBox.ValueChanged += (_, _) => Validate();
        dialog.Opened += (_, _) => widthBox.Focus(FocusState.Programmatic);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return ((int)widthBox.Value, (int)heightBox.Value);
    }

    // ---- 拡大して書き出し ----

    private async Task ExportScaledAsync()
    {
        if (CurrentTab is not { } tab)
        {
            return;
        }

        CommitFloating(tab);
        PixelDocument document = tab.Document;
        int maxScale = PixelImage.MaxSize / Math.Max(document.Width, document.Height);
        var choices = new RadioButtons { Header = "倍率", MaxColumns = 4 };
        foreach (int scale in ExportScales.Where(s => s <= Math.Max(1, maxScale)))
        {
            choices.Items.Add($"×{scale}（{document.Width * scale}×{document.Height * scale}）");
        }

        int last = ViewSettings.GetDouble("export-scale") is { } saved ? (int)saved : 4;
        int[] available = ExportScales.Where(s => s <= Math.Max(1, maxScale)).ToArray();
        choices.SelectedIndex = Math.Max(0, Array.IndexOf(available, available.LastOrDefault(s => s <= last, 1)));

        var panel = new StackPanel { Spacing = 12, MinWidth = 320 };
        panel.Children.Add(choices);
        panel.Children.Add(new TextBlock
        {
            Text = "表示中のレイヤーを重ねた見た目を、ドットのまま拡大して PNG で書き出します。今開いているファイルはそのままです。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "拡大して書き出し",
            Content = panel,
            PrimaryButtonText = "書き出す",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || choices.SelectedIndex < 0)
        {
            return;
        }

        int chosen = available[choices.SelectedIndex];
        ViewSettings.SetDouble("export-scale", chosen);

        string baseName = Path.GetFileNameWithoutExtension(document.Name);
        var picker = new FileSavePicker { SuggestedFileName = chosen > 1 ? $"{baseName}@{chosen}x" : baseName };
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeChoices.Add("PNG 画像", [".png"]);
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            await PngFile.SaveAsync(document, file, chosen);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("書き出せませんでした", ex.Message);
        }
    }
}
