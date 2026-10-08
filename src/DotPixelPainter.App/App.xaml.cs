using Microsoft.UI.Xaml;

namespace DotPixelPainter;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(StartupProbe.FromCommandLine());
        _window.Activate();
    }
}
