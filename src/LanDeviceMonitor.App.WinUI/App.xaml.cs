using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace LanDeviceMonitor;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(
        LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
