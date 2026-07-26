using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using LanDeviceMonitor.Services;

namespace LanDeviceMonitor;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private TrayService? _trayService;
    private NotificationService? _notificationService;
    private SingleInstanceService? _singleInstanceService;
    private WindowPlacementService? _windowPlacementService;
    private bool _hasExited;

    private void ComboBoxDropDown_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            SmoothScrollBehavior.TryHandle(source, e))
        {
            e.Handled = true;
        }
    }

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
            Environment.Exit(0);
            return;
        }

        _singleInstanceService.StartListening(() =>
            Dispatcher.BeginInvoke(ShowMainWindow));

        _notificationService = new NotificationService();
        _notificationService.Register();

        _mainWindow = new MainWindow(_notificationService);
        _windowPlacementService = new WindowPlacementService(_mainWindow);
        _mainWindow.SourceInitialized += (_, _) =>
        {
            _windowPlacementService.Restore();
            MicaWindowService.Apply(
                _mainWindow,
                ThemeService.ResolveIsDark(ThemeService.CurrentMode));
        };
        _trayService = new TrayService(
            showWindow: ShowMainWindow,
            exitApplication: ExitApplication);
        _notificationService.UseFallback(_trayService.ShowSystemNotification);

        _mainWindow.MinimizeToTrayRequested += (_, _) =>
        {
            _windowPlacementService.SaveNow();
            _mainWindow.Hide();
        };
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

    private void ExitApplication()
    {
        if (_hasExited)
        {
            return;
        }

        _hasExited = true;
        ArmForcedExitFallback();
        _windowPlacementService?.SaveNow();
        _mainWindow?.PrepareForExit();
        _trayService?.PrepareForProcessExit();
        _trayService = null;

        // Windows App SDK 的通知注销在部分系统上会阻塞数秒，甚至让进程继续存活。
        // 退出时不再调用它；进程结束后 Windows 会回收该进程的通知注册资源。
        _notificationService = null;

        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
        _windowPlacementService?.Dispose();
        _windowPlacementService = null;

        // 不再先进入可能等待窗口和 COM 组件的 WPF Shutdown 流程。
        Environment.Exit(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_hasExited)
        {
            _hasExited = true;
            _windowPlacementService?.SaveNow();
            _mainWindow?.PrepareForExit();
            _trayService?.Dispose();
            _singleInstanceService?.Dispose();
            _windowPlacementService?.Dispose();
        }

        base.OnExit(e);
    }

    private static void ArmForcedExitFallback()
    {
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(900);
            _ = TerminateProcess(GetCurrentProcess(), 0);
        })
        {
            IsBackground = true,
            Name = "LanDeviceMonitor.ExitWatchdog"
        };
        watchdog.Start();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr processHandle, uint exitCode);
}
