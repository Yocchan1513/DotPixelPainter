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

        // GPU デバイスの作成（数十ms）を、画面部品の準備と並行して裏で進める
        Task.Run(static () => Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice());

        InitializeComponent();
        StartupProbe.Current.Mark("app-resources");

        UnhandledException += (_, e) => ErrorLog.Write("UnhandledException", e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => ErrorLog.Write("UnobservedTaskException", e.Exception);
    }

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
        window.DispatcherQueue.TryEnqueue(() => window.OnRedirected(files));
    }
}
