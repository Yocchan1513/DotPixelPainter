using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DotPixelPainter;

/// <summary>
/// 右のパネルをたたむ。ツールバーのボタンか「表示」メニューで切り替え、ウィンドウが狭いときは自動でたたむ。
/// 自分で隠したときは、広げても隠したまま（view.txt に覚える）。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>これより狭いと（DIP）、右のパネルを自動でたたむ。</summary>
    private const double NarrowWidth = 760;

    private const double RightPanelWidth = 232;

    private bool? _wasNarrow;

    /// <summary>自分で選んだ表示（広いときに使う）。</summary>
    private bool PanelPreferred => ViewSettings.Get("right-panel") != "hidden";

    private void PanelToggle_Click(object sender, RoutedEventArgs e)
    {
        bool show = PanelToggle.IsChecked == true;
        // 狭くて自動でたたんでいるときに出したのは、その場だけ（覚えている選択は変えない）
        if (_wasNarrow != true || !show)
        {
            ViewSettings.Set("right-panel", show ? "shown" : "hidden");
        }

        ApplyRightPanel(show);
    }

    /// <summary>ウィンドウの幅が「狭い／広い」の境目をまたいだときだけ、表示を切り替える。</summary>
    private void UpdatePanelForWidth(double width)
    {
        bool narrow = width < NarrowWidth;
        if (narrow == _wasNarrow)
        {
            return;
        }

        _wasNarrow = narrow;
        bool show = !narrow && PanelPreferred;
        PanelToggle.IsChecked = show;
        ApplyRightPanel(show);
    }

    private void ApplyRightPanel(bool show)
    {
        RightPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RightColumn.Width = new GridLength(show ? RightPanelWidth : 0);
    }
}
