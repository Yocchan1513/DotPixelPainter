using DotPixelPainter.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DotPixelPainter;

/// <summary>
/// ディザ（網かけ）のパターン塗り。ペン・図形・塗りつぶしに効く。
/// 「すき間を背景色で塗る」がオンなら、模様のすき間を背景色で塗る（消しゴムのときは使わない）。
/// </summary>
public sealed partial class MainWindow
{
    private bool _patternItemsAdded;

    /// <summary>パターンの選択肢を足す（起動を軽くするため、最初の描画のあとに呼ぶ）。</summary>
    private void AddPatternItems()
    {
        if (_patternItemsAdded)
        {
            return;
        }

        _patternItemsAdded = true;
        foreach (DitherPattern pattern in DitherPattern.Presets)
        {
            PatternBox.Items.Add(new ComboBoxItem { Content = pattern.Name });
        }
    }

    /// <summary>今選んでいるパターン（なしなら null）。erase なら、すき間の色は使わない。</summary>
    private DrawPattern? CurrentPattern(bool erase)
    {
        int index = PatternBox.SelectedIndex - 1; // 先頭は「なし」
        if (index < 0 || index >= DitherPattern.Presets.Count)
        {
            return null;
        }

        uint? gap = !erase && PatternGapBox.IsChecked == true ? _backColor : null;
        return new DrawPattern(DitherPattern.Presets[index], gap);
    }

    private void Pattern_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // XAML の読み込み中にも呼ばれる
        if (PatternGapBox is null)
        {
            return;
        }

        PatternGapBox.Visibility = PatternBox.SelectedIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus(null);
    }

    private void PatternGap_Click(object sender, RoutedEventArgs e) => UpdateStatus(null);

    private string PatternStatus() => PatternBox?.SelectedIndex > 0 && PatternBox.SelectedItem is ComboBoxItem { Content: string name }
        ? $"・パターン {name}{(PatternGapBox.IsChecked == true ? "（すき間は背景色）" : "")}"
        : "";
}
