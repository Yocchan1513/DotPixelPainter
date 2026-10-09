using DotPixelPainter.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DotPixelPainter;

/// <summary>新規作成ダイアログ（サイズと背景を選ぶ）。</summary>
public sealed partial class MainWindow
{
    private static readonly int[] PresetSizes = [16, 24, 32, 48, 64, 128, 256];

    /// <summary>「新規」ボタンと Ctrl+N。ダイアログで選んだサイズは次回の既定値になる。</summary>
    private async Task NewDocumentWithDialogAsync()
    {
        NewDocumentSettings last = NewDocumentSettings.Load();

        var widthBox = SizeBox("幅（px）", last.Width);
        var heightBox = SizeBox("高さ（px）", last.Height);

        var swap = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 },
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(8),
        };
        ToolTipService.SetToolTip(swap, "幅と高さを入れ替え");
        swap.Click += (_, _) => (widthBox.Value, heightBox.Value) = (heightBox.Value, widthBox.Value);

        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sizeRow.Children.Add(widthBox);
        sizeRow.Children.Add(swap);
        sizeRow.Children.Add(heightBox);

        var presets = new VariableSizedWrapGrid
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 84,
            ItemHeight = 38,
            MaximumRowsOrColumns = 4,
        };
        // Minecraft スキンのボタンを押したときだけ、作ったあとスキンモードにする（null ならふつうの画像）
        bool? skinSlim = null;
        var skinNote = new TextBlock { FontSize = 12, Visibility = Visibility.Collapsed };

        foreach (int size in PresetSizes)
        {
            var preset = new Button { Content = $"{size}×{size}", HorizontalAlignment = HorizontalAlignment.Stretch };
            preset.Click += (_, _) =>
            {
                widthBox.Value = size;
                heightBox.Value = size;
                skinSlim = null;
                skinNote.Visibility = Visibility.Collapsed;
            };
            presets.Children.Add(preset);
        }

        var skinRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach ((string label, bool slim) in new[] { ("Minecraft スキン（クラシック）", false), ("スリム", true) })
        {
            var button = new Button { Content = label };
            button.Click += (_, _) =>
            {
                widthBox.Value = 64;
                heightBox.Value = 64;
                skinSlim = slim;
                skinNote.Text = $"64×64 のスキンとして作ります（腕: {(slim ? "スリム 3px" : "クラシック 4px")}）";
                skinNote.Visibility = Visibility.Visible;
            };
            skinRow.Children.Add(button);
        }

        var background = new RadioButtons { Header = "背景", MaxColumns = 2 };
        background.Items.Add("透明");
        background.Items.Add("白");
        background.SelectedIndex = last.WhiteBackground ? 1 : 0;

        var panel = new StackPanel { Spacing = 12, MinWidth = 340 };
        panel.Children.Add(sizeRow);
        panel.Children.Add(new TextBlock
        {
            Text = $"よく使うサイズ（最大 {PixelImage.MaxSize}×{PixelImage.MaxSize}）",
            // 注意: Resources["CaptionTextBlockStyle"] を (Style) に変換すると Native AOT では InvalidCastException になる
            FontSize = 12,
        });
        panel.Children.Add(presets);
        panel.Children.Add(skinRow);
        panel.Children.Add(skinNote);
        panel.Children.Add(background);

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "新規作成",
            Content = panel,
            PrimaryButtonText = "作成",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
        };

        void Validate() => dialog.IsPrimaryButtonEnabled = IsValid(widthBox.Value) && IsValid(heightBox.Value);
        widthBox.ValueChanged += (_, _) => Validate();
        heightBox.ValueChanged += (_, _) => Validate();
        dialog.Opened += (_, _) => widthBox.Focus(FocusState.Programmatic);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var chosen = new NewDocumentSettings((int)widthBox.Value, (int)heightBox.Value, background.SelectedIndex == 1);
        chosen.Save();
        PixelDocument document = CreateUntitled(chosen.Width, chosen.Height, chosen.WhiteBackground);
        if (skinSlim is { } slimArms && SkinLayout.IsSkinSize(chosen.Width, chosen.Height))
        {
            document.SkinMode = true;
            document.SlimArms = slimArms;
        }

        AddTab(document);
    }

    private static NumberBox SizeBox(string header, int value) => new()
    {
        Header = header,
        Value = value,
        Minimum = 1,
        Maximum = PixelImage.MaxSize,
        SmallChange = 1,
        LargeChange = 8,
        Width = 140,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
    };

    private static bool IsValid(double value) =>
        !double.IsNaN(value) && value == Math.Floor(value) && NewDocumentSettings.IsValidSize((int)value);
}
