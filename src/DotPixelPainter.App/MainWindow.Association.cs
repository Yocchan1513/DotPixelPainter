using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using WinRT.Interop;

namespace DotPixelPainter;

/// <summary>ファイルの関連付けの切り替えと、ほかの起動から送られてきたファイルの受け取り。</summary>
public sealed partial class MainWindow
{
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

        UpdateAssociationItem();
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
