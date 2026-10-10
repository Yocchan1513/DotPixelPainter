using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DotPixelPainter;

/// <summary>
/// 「ヘルプ → バージョン情報」。アイコンと版を出し、GitHub の latest-version.txt を読んで最新かどうかを知らせる。
/// 通信するのはこの画面を開いたときだけ（起動時には確かめない）。
/// </summary>
public sealed partial class MainWindow
{
    private const string LatestVersionUrl = "https://raw.githubusercontent.com/Yocchan1513/DotPixelPainter/main/latest-version.txt";
    private const string ReleasesUrl = "https://github.com/Yocchan1513/DotPixelPainter/releases";

    private async Task ShowAboutAsync()
    {
        var status = new TextBlock { Text = "最新バージョンを確認しています…", TextWrapping = TextWrapping.Wrap };
        var download = new HyperlinkButton
        {
            Content = "ダウンロードのページを開く",
            NavigateUri = new Uri(ReleasesUrl),
            Padding = new Thickness(0),
            Visibility = Visibility.Collapsed,
        };

        var text = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = "DotPixelPainter", FontSize = 20, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = $"バージョン {AppVersion.TrimStart('v')}" });
        text.Children.Add(new TextBlock { Text = "ドット絵とMinecraftスキンのためのお絵かきソフト", TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = "© 2026 Yocchan1513　MIT License", FontSize = 12, Opacity = 0.8 });
        text.Children.Add(status);
        text.Children.Add(download);

        var logo = new Image
        {
            Width = 72,
            Height = 72,
            VerticalAlignment = VerticalAlignment.Top,
            Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "DotPixelPainter-192.png"))),
        };

        // 横並びの StackPanel だと文が折り返さないので、右の列を残りの幅にする
        var content = new Grid { ColumnSpacing = 20 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(text, 1);
        content.Children.Add(logo);
        content.Children.Add(text);

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Content = content,
            CloseButtonText = "OK",
        };

        // 表示してから裏で確かめる（つながらなくても画面はすぐ出す）
        Task shown = dialog.ShowAsync().AsTask();
        (string message, bool newer) = await CheckLatestVersionAsync();
        status.Text = message;
        download.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
        await shown;
    }

    private static async Task<(string Message, bool Newer)> CheckLatestVersionAsync()
    {
        try
        {
            using var http = new Windows.Web.Http.HttpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string text = await http.GetStringAsync(new Uri(LatestVersionUrl)).AsTask(timeout.Token);
            if (!Version.TryParse(text.Trim().TrimStart('v'), out Version? latest)
                || !Version.TryParse(AppVersion.TrimStart('v'), out Version? current))
            {
                return ("最新バージョンを確認できませんでした。", false);
            }

            return latest > current
                ? ($"新しいバージョン {latest} があります。", true)
                : ("最新バージョンを使用しています。", false);
        }
        catch (Exception)
        {
            return ("最新バージョンを確認できませんでした。", false);
        }
    }
}
