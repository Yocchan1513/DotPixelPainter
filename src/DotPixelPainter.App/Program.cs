using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace DotPixelPainter;

/// <summary>
/// アプリの入り口（XAML が自動で作る入り口の代わり）。
/// すでに DotPixelPainter が開いていれば、渡されたファイルをそちらに送って、このプロセスはすぐ終わる。
/// （.dotpix をダブルクリックしたとき、ウィンドウが増えずに今の画面のタブで開くように）
/// </summary>
public static class Program
{
    private const string InstanceKey = "DotPixelPainter.Main";

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // 起動時間の計測中は、毎回新しく起動する
        bool measuring = args.Contains("--startup-log");
        if (!measuring && RedirectToRunningInstance())
        {
            return 0;
        }

        Application.Start(callback =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    /// <summary>ほかの DotPixelPainter が動いていれば、そちらに起動の内容を送って true を返す。</summary>
    private static bool RedirectToRunningInstance()
    {
        AppInstance main = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (main.IsCurrent)
        {
            main.Activated += (_, e) => App.OnRedirected(e);
            return false;
        }

        // 送り終わるまで待つ（UI スレッドでない所で待つ必要がある）
        AppActivationArguments activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        using var done = new ManualResetEventSlim();
        Task.Run(async () =>
        {
            try
            {
                await main.RedirectActivationToAsync(activation);
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait(TimeSpan.FromSeconds(10));
        return true;
    }
}
