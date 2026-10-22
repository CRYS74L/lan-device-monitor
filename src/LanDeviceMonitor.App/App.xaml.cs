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
    private bool _isShowingMainWindow;

    internal static bool ShouldShowMainWindow(
        bool hasExited,
        bool hasWindow,
        bool isVisible) =>
        !hasExited && hasWindow && !isVisible;

    private void ComboBoxDropDown_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            SmoothScrollBehavior.TryHandleDropDown(source, e))
        {
            e.Handled = true;
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // This elevated helper only configures the current executable's narrow LAN
        // rule. It must run before the ordinary single-instance/window lifecycle.
        if (e.Args.FirstOrDefault() == Services.WebAccess.LanFirewallAccess.HelperArgument)
        {
            var code = 1;
            try
            {
                if (Services.WebAccess.LanFirewallAccess.TryParsePort(e.Args, out var port))
                {
                    Services.WebAccess.LanFirewallAccess.Configure(Environment.ProcessPath!, port);
                    code = 0;
                }
            }
            catch { }
            Environment.Exit(code);
            return;
        }
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

        _mainWindow = new MainWindow(
            _notificationService);
        try { await _mainWindow.PrepareInitialContentAsync(); }
        catch (Exception exception) { System.Diagnostics.Trace.WriteLine(exception); }
        _windowPlacementService = new WindowPlacementService(_mainWindow);
        _mainWindow.SourceInitialized += (_, _) =>
        {
            _windowPlacementService.Restore();
            MicaWindowService.Apply(
                _mainWindow,
                ThemeService.ResolveIsDark(ThemeService.CurrentMode));
        };
        _mainWindow.ContentRendered += MainWindow_ContentRendered;
        _trayService = new TrayService(
            showWindow: ShowMainWindow,
            exitApplication: ExitApplication);
        _mainWindow.MinimizeToTrayRequested += (_, _) =>
        {
            _windowPlacementService.SaveNow();
            // Window_Closing is still unwinding its native close message.
            // Hide the HWND only after that message returns. Keeping WPF's
            // IsVisible state intact preserves its redirection surface for a
            // complete first frame when the same HWND is restored.
            _mainWindow.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Normal,
                new Action(() =>
                {
                    if (!_hasExited && _mainWindow.IsVisible)
                    {
                        _mainWindow.HideToTrayPreservingVisual();
                    }
                }));
        };
        if (!startHidden)
        {
            _mainWindow.Show();
        }

        var initialization = _mainWindow.InitializeAsync();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        _notificationService.Register();
        await initialization;
    }

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ContentRendered -= MainWindow_ContentRendered;
        _mainWindow.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                if (_mainWindow is { IsVisible: true })
                {
                    MicaWindowService.Apply(
                        _mainWindow,
                        ThemeService.ResolveIsDark(ThemeService.CurrentMode));
                }
            }));
    }

    private void ShowMainWindow()
    {
        if (_isShowingMainWindow || _mainWindow is null || _hasExited)
        {
            return;
        }

        _isShowingMainWindow = true;
        try
        {
            if (ShouldShowMainWindow(_hasExited, true, _mainWindow.IsVisible))
            {
                _mainWindow.Show();
            }

            _mainWindow.RestoreFromTray();

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
        finally
        {
            _isShowingMainWindow = false;
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
        _notificationService?.Dispose();
        _notificationService = null;
        _trayService?.PrepareForProcessExit();
        _trayService = null;

        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
        _windowPlacementService?.Dispose();
        _windowPlacementService = null;
        MicaWindowService.Dispose();

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
            _notificationService?.Dispose();
            _notificationService = null;
            _trayService?.Dispose();
            _singleInstanceService?.Dispose();
            _windowPlacementService?.Dispose();
            MicaWindowService.Dispose();
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
