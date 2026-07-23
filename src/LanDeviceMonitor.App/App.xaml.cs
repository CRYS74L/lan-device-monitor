using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LanDeviceMonitor.Services;

namespace LanDeviceMonitor;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private TrayService? _trayService;
    private NotificationService? _notificationService;
    private SingleInstanceService? _singleInstanceService;
    private bool _hasExited;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var startHidden = e.Args.Contains(
            StartupRegistrationService.BackgroundArgument,
            StringComparer.OrdinalIgnoreCase);

        _singleInstanceService = new SingleInstanceService();
        if (!_singleInstanceService.IsPrimary)
        {
            await SingleInstanceService.SignalPrimaryAsync();
            _singleInstanceService.Dispose();
            _singleInstanceService = null;
            Shutdown();
            return;
        }

        _singleInstanceService.StartListening(() =>
            Dispatcher.BeginInvoke(ShowMainWindow));

        _notificationService = new NotificationService();
        _notificationService.Register();

        _mainWindow = new MainWindow(_notificationService);
        _mainWindow.SourceInitialized += (_, _) => MicaWindowService.Apply(_mainWindow);
        _trayService = new TrayService(
            showWindow: ShowMainWindow,
            exitApplication: ExitApplication);

        _mainWindow.MinimizeToTrayRequested += (_, _) => _mainWindow.Hide();
        if (!startHidden)
        {
            _mainWindow.Show();
        }

        await _mainWindow.InitializeAsync();
    }

    private void ShowMainWindow()
    {
        if (_hasExited || _mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Activate();

        var handle = new WindowInteropHelper(_mainWindow).Handle;
        if (handle != IntPtr.Zero)
        {
            _ = ShowWindow(handle, 9);
            _ = SetForegroundWindow(handle);
        }
    }

    private async void ExitApplication()
    {
        if (_hasExited)
        {
            return;
        }

        _hasExited = true;
        // 先移除所有可见元素，让“退出”在点击后立即得到视觉响应。
        _trayService?.Dispose();
        _trayService = null;
        if (_mainWindow is not null)
        {
            await _mainWindow.PrepareForExitAsync();
        }

        var notificationService = _notificationService;
        _notificationService = null;
        if (notificationService is not null)
        {
            // Windows App SDK 的注销偶尔会阻塞，放到后台线程避免卡住界面。
            try
            {
                await Task.Run(notificationService.Dispose);
            }
            catch
            {
                // 进程即将结束，系统会回收通知注册资源。
            }
        }

        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_hasExited)
        {
            _hasExited = true;
            _trayService?.Dispose();
            _notificationService?.Dispose();
            _singleInstanceService?.Dispose();
        }

        base.OnExit(e);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);
}
