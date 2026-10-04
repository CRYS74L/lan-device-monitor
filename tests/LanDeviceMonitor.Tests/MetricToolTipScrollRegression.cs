using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class MetricToolTipScrollRegression
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);

    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null; MainWindow? window = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window = new MainWindow(new NotificationService())
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, Width = 1180, Height = 630, ShowActivated = false, ShowInTaskbar = false };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                var now = DateTimeOffset.Now;
                var device = new DeviceViewData
                {
                    Profile = new DeviceProfile { MacAddress = "02:00:00:00:66:01", LastRouterName = "提示测试设备", LastKnownOnline = true, FirstSeenAt = now.AddDays(-80), LastSeenAt = now },
                    Identity = new DeviceIdentity("手机", "\uE8EA", "提示测试设备"),
                    Sessions = Enumerable.Range(0, 12).Select(i => new DeviceSession { MacAddress = "02:00:00:00:66:01", OnlineAt = now.AddHours(-2 * i - 2), OfflineAt = now.AddHours(-2 * i - 1) }).ToArray()
                };
                typeof(MainWindow).GetMethod("ReplaceDevices", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new[] { device }, false, false]);
                window.Show(); window.UpdateLayout(); Pump(100);
                Require(window.DetailPanel.ScrollableHeight > 30, "Fixture has no scrollable detail content.");
                foreach (var card in new[] { window.DownloadTotalCard, window.UploadTotalCard })
                {
                    var tip = (ToolTip)ToolTipService.GetToolTip(card);
                    tip.PlacementTarget = card;
                    tip.Opened += (_, _) =>
                    {
                        if (System.Windows.Interop.HwndSource.FromVisual(tip) is System.Windows.Interop.HwndSource source)
                            SetWindowPos(source.Handle, IntPtr.Zero, -20000, -20000, 0, 0, 0x0001 | 0x0004 | 0x0010);
                    };
                    for (var round = 0; round < 3; round++)
                    {
                        window.DetailPanel.ScrollToTop(); window.UpdateLayout(); Pump(20);
                        tip.IsOpen = true; Pump(20); Require(tip.IsOpen, "Valid tooltip did not open.");
                        // No mouse movement: the real scroll offset alone must
                        // dismiss a popup whose target is moving underneath it.
                        window.DetailPanel.ScrollToVerticalOffset(100); window.UpdateLayout(); Pump(20);
                        Require(!tip.IsOpen, $"{card.Name} tooltip remained open after stationary-pointer scrolling.");
                    }
                    window.DetailPanel.ScrollToTop(); window.UpdateLayout(); Pump(20);
                    tip.IsOpen = true; Pump(20);
                    card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent });
                    Require(!tip.IsOpen, "Mouse leave did not close the range hint.");
                    tip.IsOpen = true; Pump(20);
                    Require(SmoothScrollBehavior.TryHandle(card, new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)), "Smooth scroll was not started.");
                    Pump(80); Require(!tip.IsOpen, "Animated wheel scrolling did not close the hint."); Pump(450);
                    window.DetailPanel.ScrollToTop(); window.UpdateLayout(); Pump(20);
                    tip.IsOpen = true; Pump(20); card.Visibility = Visibility.Collapsed; Pump(20);
                    Require(!tip.IsOpen, "Hidden target left an orphaned hint.");
                    tip.IsOpen = true; Pump(20); Require(!tip.IsOpen, "An already hidden target reopened an orphaned hint.");
                    card.Visibility = Visibility.Visible; window.UpdateLayout();
                }
                Console.WriteLine("Metric tooltip lifecycle PASS: download/upload, stationary-pointer scroll, repeated reopen, mouse leave, smooth wheel and hidden target.");
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(20))) { Console.Error.WriteLine("Tooltip scroll regression timed out."); Environment.Exit(1); }
        if (failure is null) return 0; Console.Error.WriteLine(failure); return 1;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
