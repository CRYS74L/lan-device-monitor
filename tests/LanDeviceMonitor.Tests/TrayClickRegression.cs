using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Services;
using LanDeviceMonitor.Controls;

internal static class TrayClickRegression
{
    private const int TrayCallbackMessage = NativeTrayIcon.CallbackMessage;
    private const int ContextMenu = NativeTrayIcon.ContextMenuEvent, Select = NativeTrayIcon.SelectEvent, KeySelect = NativeTrayIcon.KeySelectEvent;
    private const int LeftDown = 0x0201, LeftUp = 0x0202, LeftDoubleClick = 0x0203;
    private const int RightDown = 0x0204, RightUp = 0x0205, RightDoubleClick = 0x0206;
    private const int MiddleDown = 0x0207, MiddleUp = 0x0208, MiddleDoubleClick = 0x0209;
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Run()
    {
        Exception? error = null;
        var failures = new List<string>();
        var thread = new Thread(() =>
        {
            App? app = null;
            MainWindow? window = null;
            TrayService? tray = null;
            try
            {
                // Suppress production startup: this fixture must not acquire the
                // installed monitor's mutex or signal its existing instance.
                app = TestAppFactory.Create();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window = new MainWindow(new NotificationService())
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000, Top = -10000, Width = 900, Height = 560,
                    ShowActivated = false, ShowInTaskbar = false
                };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", PrivateInstance)!.SetValue(window, true);
                typeof(App).GetField("_mainWindow", PrivateInstance)!.SetValue(app, window);
                window.MinimizeToTrayRequested += (_, _) => window.Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal, new Action(window.HideToTrayPreservingVisual));
                window.Show();
                window.UpdateLayout();
                Pump();
                var originalHandle = new WindowInteropHelper(window).Handle;
                var showCalls = 0;
                var exitCalls = 0;
                tray = new TrayService(
                    () =>
                    {
                        showCalls++;
                        typeof(App).GetMethod("ShowMainWindow", PrivateInstance)!.Invoke(app, null);
                    },
                    () => exitCalls++);
                var notifyIcon = GetIcon(tray);
                var callbackHandle = notifyIcon.Handle;

                void HideWindow()
                {
                    window.Close();
                    Pump();
                    if (IsWindowVisible(originalHandle))
                        throw new InvalidOperationException("Tray fixture failed to hide the existing HWND.");
                    showCalls = 0;
                }

                bool RestoredSameWindow() => IsWindowVisible(originalHandle) &&
                    new WindowInteropHelper(window).Handle == originalHandle;

                void Check(bool condition, string name, string observed)
                {
                    Console.WriteLine($"Tray {name}: {(condition ? "PASS" : "FAIL")} ({observed})");
                    if (!condition) failures.Add(name);
                }

                HideWindow();
                Dispatch(callbackHandle, LeftDown, LeftUp, Select);
                Check(showCalls == 1 && RestoredSameWindow(), "left single click restores once",
                    $"showCalls={showCalls}, nativeVisible={IsWindowVisible(originalHandle)}, sameHwnd={new WindowInteropHelper(window).Handle == originalHandle}");

                HideWindow();
                Dispatch(callbackHandle, LeftDown, LeftUp, Select);
                Check(showCalls == 1 && RestoredSameWindow(), "double click opens on first release",
                    $"showCalls={showCalls}, nativeVisible={IsWindowVisible(originalHandle)}");
                Dispatch(callbackHandle, LeftDoubleClick, LeftUp);
                Check(showCalls == 1 && RestoredSameWindow(), "left double click does not activate twice",
                    $"showCalls={showCalls}");
                Dispatch(callbackHandle, LeftDown, LeftUp, Select);
                Check(showCalls == 2 && RestoredSameWindow(), "later single click still activates",
                    $"showCalls={showCalls}");

                HideWindow();
                Dispatch(callbackHandle, KeySelect);
                Check(showCalls == 1 && RestoredSameWindow(), "V4 keyboard activation", $"showCalls={showCalls}");

                HideWindow();
                Dispatch(callbackHandle, MiddleDown, MiddleUp, MiddleDoubleClick, MiddleUp);
                Check(showCalls == 0 && !IsWindowVisible(originalHandle), "middle clicks do not open",
                    $"showCalls={showCalls}, nativeVisible={IsWindowVisible(originalHandle)}");

                HideWindow();
                Dispatch(callbackHandle, RightDown, RightUp);
                Check(GetMenu(tray) is null, "V4 raw right release cannot enter legacy menu mode", "menuPresent=" + (GetMenu(tray) is not null));
                Dispatch(callbackHandle, ContextMenu);
                var menu = GetMenu(tray);
                var anchor = menu!.PlacementTarget.PointToScreen(new System.Windows.Point());
                Check(Math.Abs(anchor.X+800)<1 && Math.Abs(anchor.Y-550)<1,
                    "V4 packed callback preserves the captured screen anchor", $"anchor={anchor}");
                Dispatch(callbackHandle, 0x0407); // NIN_POPUPCLOSE: pointer left the tray icon.
                Check(menu.IsOpen && ReferenceEquals(menu,GetMenu(tray)), "pointer leave notification does not dismiss the command menu", "open=" + menu.IsOpen);
                Check(showCalls == 0 && !IsWindowVisible(originalHandle) && menu is { IsOpen: true },
                    "right single click opens only the menu", $"showCalls={showCalls}, menuVisible={menu?.IsOpen}");
                Dispatch(callbackHandle, RightDoubleClick, RightUp, ContextMenu);
                menu = GetMenu(tray);
                Check(showCalls == 0 && !IsWindowVisible(originalHandle) && menu is not { IsOpen: true },
                    "second right click toggles the ReTray menu closed", $"showCalls={showCalls}, menuVisible={menu?.IsOpen}");
                Pump(400);
                Dispatch(callbackHandle, RightDown, RightUp, ContextMenu);
                menu = GetMenu(tray);

                if (menu is null) throw new InvalidOperationException("Right-click menu missing for command verification.");
                ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Pump(400);
                Check(showCalls == 1 && RestoredSameWindow() && GetMenu(tray) is null,
                    "menu open restores the existing window", $"showCalls={showCalls}, sameHwnd={new WindowInteropHelper(window).Handle == originalHandle}");

                HideWindow();
                Dispatch(callbackHandle, RightDown, RightUp, ContextMenu);
                menu = GetMenu(tray) ?? throw new InvalidOperationException("Exit menu missing.");
                ((MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(exitCalls == 1 && showCalls == 0, "menu exit keeps its own callback",
                    $"exitCalls={exitCalls}, showCalls={showCalls}");
                menu.IsOpen = false;

                tray.Dispose();
                Pump(400);
                Dispatch(callbackHandle, Select, ContextMenu);
                Check(showCalls == 0 && GetMenu(tray) is null, "dispose detaches activation and menu callbacks",
                    $"showCalls={showCalls}, menuPresent={GetMenu(tray) is not null}");
                tray = null;

                using var exitingTray = new TrayService(() => showCalls++, () => exitCalls++);
                var exitingIcon = GetIcon(exitingTray);
                var exitHandle = exitingIcon.Handle;
                exitingTray.PrepareForProcessExit();
                Dispatch(exitHandle, Select, ContextMenu);
                Check(showCalls == 0 && GetMenu(exitingTray) is null && !exitingIcon.IsAdded,
                    "process exit removes the shell icon and native callback",
                    $"showCalls={showCalls}, menuPresent={GetMenu(exitingTray) is not null}, added={exitingIcon.IsAdded}");
            }
            catch (Exception exception) { error = exception.GetBaseException(); }
            finally
            {
                tray?.Dispose();
                if (window is not null) { window.PrepareForExit(); window.Close(); }
                app?.Shutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45)))
        {
            Console.Error.WriteLine("Tray click regression timed out.");
            return 1;
        }
        if (error is not null) { Console.Error.WriteLine(error); return 1; }
        if (failures.Count > 0)
        {
            Console.Error.WriteLine($"Tray click regression failed: {string.Join("; ", failures)}");
            return 1;
        }
        Console.WriteLine("Tray click regression PASS: native V4 selection, WM_CONTEXTMENU anchors and callback HWND lifecycle.");
        return 0;
    }

    private static NativeTrayIcon GetIcon(TrayService tray) =>
        (NativeTrayIcon)typeof(TrayService).GetField("_notifyIcon", PrivateInstance)!.GetValue(tray)!;

    private static ContextMenu? GetMenu(TrayService tray) =>
        ((TrayCommandMenu)typeof(TrayService).GetField("_menu", PrivateInstance)!.GetValue(tray)!).CurrentMenu;

    private static void Dispatch(nint callback, params int[] events)
    {
        // Deliver the actual V4 packed callback through its native HWND.
        // Signed coordinates deliberately cover monitors left of the primary.
        foreach (var kind in events) {
            var word = (nint)(unchecked((ushort)-800) | (550 << 16));
            var value = (nint)((1 << 16) | kind);
            SendMessage(callback, TrayCallbackMessage, word, value);
        }
        Pump();
    }

    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, int message, nint word, nint value);

    private static void Pump(int milliseconds = 0)
    {
        if (milliseconds > 0) {
            var timedFrame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); timedFrame.Continue=false; };
            timer.Start(); Dispatcher.PushFrame(timedFrame); return;
        }
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);
}
