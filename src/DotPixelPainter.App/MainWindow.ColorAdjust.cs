using DotPixelPainter.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DotPixelPainter;

/// <summary>
/// 「画像」メニューの色の操作: 色の置き換え、減色、使っている色をパレットに取り込む。
/// 選択範囲があればその中だけ、なければ全体に効く。どれも元に戻せる。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>ダイアログに並べる色の数の上限（多すぎると選べないので、多い順にここまで）。</summary>
    private const int MaxListedColors = 96;

    // ---- 色の置き換え ----

    private async Task ShowReplaceColorDialogAsync()
    {
        if (CurrentTab is not { } tab)
        {
            return;
        }

        CommitFloating(tab);
        PixelDocument document = tab.Document;
        PixelRect? area = tab.Selection;
        List<uint> used = ColorTools.CountColors(document.Layers.Select(l => l.Image), area)
            .Take(MaxListedColors).Select(c => c.Color).ToList();
        if (used.Count == 0)
        {
            await ShowMessageAsync("色の置き換え", area is null ? "絵に色がありません（すべて透明です）。" : "選択範囲の中に色がありません。");
            return;
        }

        // 元の色は、描画色が使われていればそれ、なければいちばん多い色から始める
        var (fromPanel, getFrom) = SwatchPicker(used, used.Contains(_color) ? _color : used[0]);
        var targets = new List<uint> { _color, _backColor };
        targets.AddRange(_palette.Colors.Where(c => !targets.Contains(c)));
        var (toPanel, getTo) = SwatchPicker(targets, _color);

        var allLayers = new CheckBox { Content = "すべてのレイヤー（オフなら今のレイヤーだけ）" };

        var panel = new StackPanel { Spacing = 8, MinWidth = 340 };
        panel.Children.Add(Caption(area is null ? "置き換える色（絵の中で使っている色）" : "置き換える色（選択範囲の中で使っている色）"));
        panel.Children.Add(fromPanel);
        panel.Children.Add(Caption("新しい色（描画色・背景色・パレット）"));
        panel.Children.Add(toPanel);
        panel.Children.Add(allLayers);

        if (!await ConfirmDialogAsync("色の置き換え", panel, "置き換える"))
        {
            return;
        }

        uint from = getFrom();
        uint to = getTo();
        bool all = allLayers.IsChecked == true;
        ImageAction(d => d.ReplaceColor(from, to, all, area) > 0);
    }

    // ---- 減色 ----

    private async Task ShowReduceColorsDialogAsync()
    {
        if (CurrentTab is not { } tab)
        {
            return;
        }

        CommitFloating(tab);
        PixelDocument document = tab.Document;
        PixelRect? area = tab.Selection;

        var mode = new RadioButtons();
        mode.Items.Add("色数を決めて減らす（代表の色は自動で選ぶ）");
        mode.Items.Add("今のパレットの色に合わせる");
        mode.SelectedIndex = 0;

        var countBox = new NumberBox
        {
            Header = "色数",
            Value = ViewSettings.GetDouble("reduce-colors") ?? 16,
            Minimum = 2,
            Maximum = 256,
            SmallChange = 1,
            LargeChange = 8,
            Width = 140,
            HorizontalAlignment = HorizontalAlignment.Left,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
        };
        var toPalette = new CheckBox { Content = "減らした色をパレットにする（今のパレットは置き換わる）" };
        var allLayers = new CheckBox { Content = "すべてのレイヤー（オフなら今のレイヤーだけ）" };
        mode.SelectionChanged += (_, _) =>
        {
            bool byCount = mode.SelectedIndex == 0;
            countBox.Visibility = byCount ? Visibility.Visible : Visibility.Collapsed;
            toPalette.Visibility = countBox.Visibility;
        };

        int usedCount = ColorTools.CountColors(document.Layers.Select(l => l.Image), area).Count;
        var panel = new StackPanel { Spacing = 10, MinWidth = 340 };
        panel.Children.Add(Caption($"今使っている色: {usedCount} 色{(area is null ? "" : "（選択範囲の中）")}"));
        panel.Children.Add(mode);
        panel.Children.Add(countBox);
        panel.Children.Add(toPalette);
        panel.Children.Add(allLayers);
        panel.Children.Add(Caption("それぞれの画素を、いちばん近い色に置き換えます。不透明度はそのままです。"));

        if (!await ConfirmDialogAsync("減色", panel, "減らす"))
        {
            return;
        }

        bool all = allLayers.IsChecked == true;
        IEnumerable<PixelImage> images = all ? document.Layers.Select(l => l.Image) : [document.ActiveLayer.Image];
        List<uint> colors;
        if (mode.SelectedIndex == 0)
        {
            int count = double.IsNaN(countBox.Value) ? 16 : (int)countBox.Value;
            ViewSettings.SetDouble("reduce-colors", count);
            colors = ColorTools.MedianCut(ColorTools.CountColors(images, area), count);
            if (toPalette.IsChecked == true && colors.Count > 0)
            {
                ReplacePalette(colors);
            }
        }
        else
        {
            colors = _palette.Colors.Select(c => c | 0xFF000000).Distinct().ToList();
        }

        ImageAction(d => d.ReduceColors(colors, all, area) > 0);
    }

    // ---- 使っている色をパレットに取り込む ----

    private async Task ImportUsedColorsAsync()
    {
        if (CurrentTab is not { } tab)
        {
            return;
        }

        PixelRect? area = tab.Selection;
        List<uint> used = ColorTools.CountColors(tab.Document.Layers.Select(l => l.Image), area)
            .Select(c => c.Color).Take(Palette.MaxColors).ToList();
        if (used.Count == 0)
        {
            await ShowMessageAsync("パレットに取り込む", "絵に色がありません（すべて透明です）。");
            return;
        }

        int newColors = used.Count(c => !_palette.Colors.Contains(c));
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "使っている色をパレットに取り込む",
            Content = new TextBlock
            {
                Text = $"{(area is null ? "絵" : "選択範囲")}で使っている {used.Count} 色（多い順）を取り込みます。\n" +
                       $"「追加」はパレットにない {newColors} 色を後ろに足し、「置き換え」はパレットをこの色だけにします。\n" +
                       $"（パレットは {Palette.MaxColors} 色まで）",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "追加",
            SecondaryButtonText = "置き換え",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
        };

        switch (await dialog.ShowAsync())
        {
            case ContentDialogResult.Primary:
                foreach (uint c in used.Where(c => !_palette.Colors.Contains(c)))
                {
                    if (!_palette.Add(c))
                    {
                        break;
                    }
                }

                OnPaletteChanged();
                break;
            case ContentDialogResult.Secondary:
                ReplacePalette(used);
                break;
        }
    }

    private void ImportUsedColors_Click(object sender, RoutedEventArgs e) => ErrorLog.Run("パレットに取り込む", ImportUsedColorsAsync);

    private void ReplacePalette(IEnumerable<uint> colors)
    {
        _palette.Clear();
        foreach (uint c in colors)
        {
            if (!_palette.Add(c))
            {
                break;
            }
        }

        OnPaletteChanged();
    }

    // ---- 部品 ----

    /// <summary>色を並べて1つ選ばせる。選んだ色は枠で示す。(並べた部品, 選んだ色を返す関数) を返す。</summary>
    private static (FrameworkElement Panel, Func<uint> Selected) SwatchPicker(IReadOnlyList<uint> colors, uint initial)
    {
        uint selected = colors.Contains(initial) ? initial : colors[0];
        var grid = new VariableSizedWrapGrid
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 28,
            ItemHeight = 28,
            MaximumRowsOrColumns = 12,
        };
        var swatches = new List<(Border Swatch, uint Color)>();
        // 注意: Application.Current.Resources の値を型変換すると Native AOT で失敗することがあるので、色は直接持つ
        var accent = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 194, 255));
        var normal = new SolidColorBrush(Colors.Gray);

        void Refresh()
        {
            foreach (var (swatch, color) in swatches)
            {
                bool on = color == selected;
                swatch.BorderBrush = on ? accent : normal;
                swatch.BorderThickness = new Thickness(on ? 3 : 1);
            }
        }

        foreach (uint color in colors)
        {
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(ToColor(color)),
            };
            ToolTipService.SetToolTip(swatch, $"#{color:X8}");
            uint c = color;
            swatch.Tapped += (_, _) =>
            {
                selected = c;
                Refresh();
            };
            swatches.Add((swatch, color));
            grid.Children.Add(swatch);
        }

        Refresh();
        var scroller = new ScrollViewer { Content = grid, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return (scroller, () => selected);
    }

    private static TextBlock Caption(string text) => new() { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left };

    private async Task<bool> ConfirmDialogAsync(string title, UIElement content, string primary)
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
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
