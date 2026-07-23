using System.Windows;
using System.Windows.Media.Animation;

namespace LanDeviceMonitor;

public partial class TrayMenuWindow : Window
{
    private readonly Action _showWindow;
    private readonly Action _exitApplication;

    public TrayMenuWindow(Action showWindow, Action exitApplication)
    {
        InitializeComponent();
        _showWindow = showWindow;
        _exitApplication = exitApplication;
        Opacity = 0;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Opacity = 1;
            return;
        }

        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _showWindow();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _exitApplication();
    }

    private void Window_Deactivated(object? sender, EventArgs e) => Close();
}
