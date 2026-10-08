using Microsoft.UI.Xaml;

namespace DotPixelPainter;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        StartupProbe.Current.Mark("app");

        // GPU デバイスの作成（数十ms）を、画面部品の準備と並行して裏で進める
        Task.Run(static () => Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice());

        InitializeComponent();
        StartupProbe.Current.Mark("app-resources");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupProbe.Current.Mark("launched");
        _window = new MainWindow(StartupProbe.Current);
        StartupProbe.Current.Mark("window-built");
        _window.Activate();
        StartupProbe.Current.Mark("activated");
        _window.ExtendIntoTitleBar();
        StartupProbe.Current.Mark("titlebar-after");
    }
}
