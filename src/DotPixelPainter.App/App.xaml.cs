using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using ILaunchActivatedEventArgs = Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs;

namespace DotPixelPainter;

public partial class App : Application
{
    private static App? _current;
    private MainWindow? _window;

    public App()
    {
        _current = this;
        StartupProbe.Current.Mark("app");

        // 言語は画面を作る前に決める（画面の文字はこれを見て選ばれる）
        Core.Lang.Japanese = LanguageSetting.UseJapanese();

        // GPU デバイスの作成（数十ms）を、画面部品の準備と並行して裏で進める
        Task.Run(static () => Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice());

        InitializeComponent();
        StartupProbe.Current.Mark("app-resources");

        // 画面の部品の想定外のエラーでアプリごと落ちないようにする（記録して続け、控えを取って一度だけ知らせる）
        UnhandledException += (_, e) =>
        {
            ErrorLog.Write("UnhandledException", e.Exception);
            e.Handled = true;
            _window?.OnUnhandledError();
        };
        TaskScheduler.UnobservedTaskException += (_, e) => ErrorLog.Write("UnobservedTaskException", e.Exception);

        // それでも落ちるとき（受け止められないエラー）は、最後に記録と控えだけでも残す
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            ErrorLog.Write("致命的なエラー", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
            _window?.EmergencyBackup();
        };
    }

    /// <summary>想定外のエラーを受け止めたとき（控えを取って一度だけ知らせる）。</summary>
    internal static void NotifyError() => _current?._window?.OnUnhandledError();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupProbe.Current.Mark("launched");
        _window = new MainWindow(StartupProbe.Current);
        StartupProbe.Current.Mark("window-built");
        _window.Activate();
        StartupProbe.Current.Mark("activated");
    }

    /// <summary>
    /// 2つ目の DotPixelPainter から送られてきた起動（.dotpix のダブルクリックなど）。
    /// 裏のスレッドで呼ばれるので、画面の処理は UI スレッドに回す。
    /// </summary>
    internal static void OnRedirected(AppActivationArguments activation)
    {
        if (_current?._window is not { } window)
        {
            return;
        }

        string commandLine = activation.Data is ILaunchActivatedEventArgs launch ? launch.Arguments : "";
        List<string> files = CommandLine.FilesIn(commandLine);
        window.DispatcherQueue.Post("送られてきたファイル", () => window.OnRedirected(files));
    }
}
