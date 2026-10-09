using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using WinRT.Interop;

namespace DotPixelPainter;

/// <summary>ツールバー右端の「…」メニューと、ほかの起動から送られてきたファイルの受け取り。</summary>
public sealed partial class MainWindow
{
    private MenuFlyout? _moreMenu;
    private MenuFlyoutItem? _associationItem;
    private ToggleMenuFlyoutItem? _restoreItem;

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (_moreMenu is null)
        {
            _moreMenu = new MenuFlyout();
            _associationItem = new MenuFlyoutItem();
            _associationItem.Click += (_, _) => ErrorLog.Run("関連付け", ToggleAssociationAsync);
            _moreMenu.Items.Add(_associationItem);

            _restoreItem = new ToggleMenuFlyoutItem { Text = "起動時に前回のタブを開き直す" };
            _restoreItem.Click += (_, _) => _restoreSession = _restoreItem.IsChecked;
            _moreMenu.Items.Add(_restoreItem);
            _moreMenu.Items.Add(new MenuFlyoutSeparator());

            var log = new MenuFlyoutItem { Text = "エラーの記録があるフォルダを開く" };
            log.Click += (_, _) =>
            {
                string folder = Path.GetDirectoryName(ErrorLog.FilePath)!;
                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            };
            _moreMenu.Items.Add(log);
        }

        _restoreItem!.IsChecked = _restoreSession;
        _associationItem!.Text = FileAssociation.IsRegistered()
            ? ".dotpix の関連付けを解除する"
            : ".dotpix をダブルクリックでこのアプリで開くようにする";
        _moreMenu.ShowAt((FrameworkElement)sender);
    }

    private async Task ToggleAssociationAsync()
    {
        bool registered = FileAssociation.IsRegistered();
        try
        {
            if (registered)
            {
                FileAssociation.Unregister();
                await ShowMessageAsync("関連付けを解除しました", ".dotpix ファイルをダブルクリックしても、このアプリでは開かなくなりました。");
            }
            else
            {
                FileAssociation.Register();
                await ShowMessageAsync(
                    "関連付けました",
                    $".dotpix ファイルをダブルクリックすると、このアプリで開きます。\n（登録したアプリの場所: {FileAssociation.ExePath}）\n\nアプリのフォルダを動かしたときは、もう一度登録してください。");
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("うまくいきませんでした", ex.Message);
        }
    }

    /// <summary>2つ目の起動から送られてきたファイルを、この画面のタブで開いて、画面を手前に出す。</summary>
    internal void OnRedirected(IReadOnlyList<string> files)
    {
        nint hwnd = WindowNative.GetWindowHandle(this);
        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, 9); // 最小化を戻す
        }

        NativeMethods.SetForegroundWindow(hwnd);
        if (files.Count > 0)
        {
            ErrorLog.Run("送られてきたファイルを開く", async () =>
            {
                foreach (string path in files)
                {
                    try
                    {
                        await OpenDocumentAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
                    }
                    catch (Exception ex)
                    {
                        await ShowMessageAsync("開けませんでした", $"{path}\n{ex.Message}");
                    }
                }
            });
        }
    }
}
