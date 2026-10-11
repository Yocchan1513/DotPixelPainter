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
                await ShowMessageAsync(T("関連付けを解除しました", "File association removed"), T(".dotpix ファイルをダブルクリックしても、このアプリでは開かなくなりました。", "Double-clicking a .dotpix file no longer opens this app."));
            }
            else
            {
                FileAssociation.Register();
                await ShowMessageAsync(
                    T("関連付けました", "File association added"),
                    T($".dotpix ファイルをダブルクリックすると、このアプリで開きます。エクスプローラーでは中身の絵（サムネイル）も出ます。\n（登録したアプリの場所: {FileAssociation.ExePath}）\n\nアプリのフォルダを動かしたときは、もう一度登録してください。", $"Double-clicking a .dotpix file now opens this app, and Explorer shows .dotpix thumbnails.\n(Registered app: {FileAssociation.ExePath})\n\nIf you move the app folder, register it again."));
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(T("うまくいきませんでした", "Something went wrong"), ex.Message);
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
                        await ShowMessageAsync(T("開けませんでした", "Could not open the file"), $"{path}\n{ex.Message}");
                    }
                }
            });
        }
    }
}
