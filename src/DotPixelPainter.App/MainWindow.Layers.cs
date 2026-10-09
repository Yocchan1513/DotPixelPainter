using DotPixelPainter.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace DotPixelPainter;

/// <summary>レイヤーパネル。一覧は小さいので、変わるたびに作り直す。</summary>
public sealed partial class MainWindow
{
    private const string VisibleGlyph = "";
    private const string HiddenGlyph = "";

    private bool _syncingLayerUi;

    /// <summary>今のタブのレイヤー一覧を作り直す（上にあるレイヤーを一覧の上に出す）。</summary>
    private void RefreshLayerList()
    {
        LayerList.Children.Clear();
        if (CurrentTab is not { } tab)
        {
            return;
        }

        PixelDocument document = tab.Document;
        var selectedBrush = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        var quietBrush = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        for (int i = document.Layers.Count - 1; i >= 0; i--)
        {
            int index = i;
            Layer layer = document.Layers[i];
            bool active = i == document.ActiveLayerIndex;

            var eye = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Child = new FontIcon { Glyph = layer.Visible ? VisibleGlyph : HiddenGlyph, FontSize = 13 },
            };
            ToolTipService.SetToolTip(eye, layer.Visible ? "非表示にする" : "表示する");
            eye.Tapped += (_, e) =>
            {
                CommitFloating(tab);
                document.SetLayerVisible(index, !document.Layers[index].Visible);
                AfterHistoryChange(tab);
                e.Handled = true;
            };

            var name = new TextBlock
            {
                Text = layer.Name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                Opacity = layer.Visible ? 1.0 : 0.5,
            };
            var opacity = new TextBlock
            {
                Text = $"{Math.Round(layer.Opacity * 100)}%",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Foreground = quietBrush,
            };

            var row = new Grid
            {
                Padding = new Thickness(2, 2, 8, 2),
                ColumnSpacing = 6,
                CornerRadius = new CornerRadius(4),
                Background = active ? selectedBrush : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(name, 1);
            Grid.SetColumn(opacity, 2);
            row.Children.Add(eye);
            row.Children.Add(name);
            row.Children.Add(opacity);
            ToolTipService.SetToolTip(row, "クリックで選択、ダブルクリックで名前を変更");

            row.Tapped += (_, _) =>
            {
                CommitFloating(tab);
                EndStroke();
                document.SelectLayer(index);
                RefreshLayerList();
                UpdateStatus(null);
            };
            row.DoubleTapped += (_, e) =>
            {
                ErrorLog.Run("レイヤー名の変更", () => RenameLayerAsync(tab, index));
                e.Handled = true;
            };

            LayerList.Children.Add(row);
        }

        _syncingLayerUi = true;
        LayerOpacitySlider.Value = Math.Round(document.ActiveLayer.Opacity * 100);
        LayerOpacityText.Text = $"不透明度 {LayerOpacitySlider.Value}%";
        _syncingLayerUi = false;
    }

    private async Task RenameLayerAsync(DocumentTab tab, int index)
    {
        var box = new TextBox { Text = tab.Document.Layers[index].Name, MinWidth = 260 };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "レイヤー名の変更",
            Content = box,
            PrimaryButtonText = "変更",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            CommitFloating(tab);
            tab.Document.RenameLayer(index, box.Text);
            AfterHistoryChange(tab);
        }
    }

    /// <summary>レイヤーの操作をして、成功したら画面を更新する。</summary>
    private void LayerAction(Func<PixelDocument, int, bool> action)
    {
        CommitFloating(CurrentTab); // 持ち上げ中の中身は、レイヤーを操作する前に置く
        EndStroke();
        if (CurrentTab is { } tab && action(tab.Document, tab.Document.ActiveLayerIndex))
        {
            AfterHistoryChange(tab);
        }
    }

    private void LayerAdd_Click(object sender, RoutedEventArgs e) => LayerAction((d, _) => d.AddLayer() is not null);

    private void LayerDuplicate_Click(object sender, RoutedEventArgs e) => LayerAction((d, i) => d.DuplicateLayer(i) is not null);

    private void LayerUp_Click(object sender, RoutedEventArgs e) => LayerAction((d, i) => d.MoveLayer(i, i + 1));

    private void LayerDown_Click(object sender, RoutedEventArgs e) => LayerAction((d, i) => d.MoveLayer(i, i - 1));

    private void LayerMerge_Click(object sender, RoutedEventArgs e) => LayerAction((d, i) => d.MergeDown(i));

    private void LayerDelete_Click(object sender, RoutedEventArgs e) => LayerAction((d, i) => d.DeleteLayer(i));

    private void LayerOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // XAML の読み込み中や、一覧の更新で値を合わせているときは何もしない
        if (_syncingLayerUi || LayerOpacityText is null || CurrentTab is not { } tab)
        {
            return;
        }

        LayerOpacityText.Text = $"不透明度 {e.NewValue}%";
        CommitFloating(tab);
        PixelDocument document = tab.Document;
        document.SetLayerOpacity(document.ActiveLayerIndex, e.NewValue / 100.0);
        AfterHistoryChange(tab);
    }
}
