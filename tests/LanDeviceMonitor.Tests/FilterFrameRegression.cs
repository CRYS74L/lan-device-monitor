using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class FilterFrameRegression
{
    internal static int Run(bool profileOnly, string? renderingProbe = null)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null; MainWindow? window = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window = new MainWindow(new NotificationService())
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 420, Top = 100, Width = 1180, Height = 900,
                    ShowActivated = renderingProbe is not null, ShowInTaskbar = false,
                    Topmost = renderingProbe is not null
                };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                var now = DateTimeOffset.Now;
                var devices = Enumerable.Range(0, 30).Select(i => new DeviceViewData
                {
                    Profile = new DeviceProfile { MacAddress = $"02:00:00:00:75:{i:X2}", LastRouterName = $"性能测试设备 {i + 1}",
                        LastKnownOnline = i < 18, LastSeenAt = now.AddMinutes(-i), FirstSeenAt = now.AddDays(-10) },
                    Identity = new DeviceIdentity("手机", "\uE8EA", $"性能测试设备 {i + 1}"),
                    Sessions = Enumerable.Range(0, 40).Select(j => new DeviceSession
                    { MacAddress = $"02:00:00:00:75:{i:X2}", OnlineAt = now.AddHours(-j - 1), OfflineAt = now.AddHours(-j).AddMinutes(-10) }).ToArray()
                }).ToArray();
                var getHistory = typeof(MainWindow).GetMethod("GetTrafficHistory", BindingFlags.Instance | BindingFlags.NonPublic)!;
                foreach (var device in devices)
                {
                    var history = (TrafficHistoryBuffer)getHistory.Invoke(window, [device.Profile.MacAddress])!;
                    for (var i = 0; i <= 120; i++) history.Append(new TrafficSample(now.AddSeconds(i * 5 - 600),
                        (long)(18000 + 17000 * Math.Sin(i * .31)), (long)(90000 + 85000 * Math.Cos(i * .21))));
                }
                typeof(MainWindow).GetMethod("ReplaceDevices", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [devices, false, false]);
                if (renderingProbe?.StartsWith("acrylic") == true)
                    window.SourceInitialized += (_, _) => MicaWindowService.Apply(window, false);
                window.Show(); window.UpdateLayout(); Pump(1000);
                Console.WriteLine($"Rendering tier={RenderCapability.Tier >> 16}, DPI={VisualTreeHelper.GetDpi(window).DpiScaleX}, probe={renderingProbe}");
                if (renderingProbe is not null)
                {
                    Pump(1000);
                    if (renderingProbe.EndsWith("idle")) { Pump(11000); return; }
                    for (var run = 0; run < 10; run++)
                    {
                        window.DeviceFilterSegments.SelectedIndex = (run + 1) % 2;
                        Pump(1100);
                    }
                    return;
                }
                var lostContainers = 0;
                foreach (var theme in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    ThemeService.Apply(theme); Pump(300);
                    for (var run = 0; run < 4; run++)
                    {
                        var frames = new List<double>(); var clock = Stopwatch.StartNew();
                        EventHandler tick = (_, _) => frames.Add(clock.Elapsed.TotalMilliseconds);
                        CompositionTarget.Rendering += tick;
                        var preparedRow = window.DeviceListTransition.ItemContainerGenerator.ContainerFromIndex(0);
                        window.DeviceFilterSegments.SelectedIndex = (run + 1) % 2;
                        var inputMilliseconds = clock.Elapsed.TotalMilliseconds;
                        var entering = window.DeviceListTransition;
                        var row = entering.ItemContainerGenerator.ContainerFromIndex(0);
                        if (!profileOnly && !ReferenceEquals(preparedRow, row))
                            throw new InvalidOperationException("The prepared rows were rebuilt on filter input.");
                        var chart = Descendants<TrafficChart>(window.DetailPanel).Single();
                        var presentation = typeof(TrafficChart).GetField("_presentationTime", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        Pump(240);
                        var duringEntrance = (DateTimeOffset)presentation.GetValue(chart)!;
                        Pump(560);
                        CompositionTarget.Rendering -= tick;
                        if ((DateTimeOffset)presentation.GetValue(chart)! <= duringEntrance)
                            throw new InvalidOperationException("The live chart stopped advancing during the transition.");
                        var retained = row is not null && ReferenceEquals(row, window.DeviceList.ItemContainerGenerator.ContainerFromIndex(0));
                        if (!retained) lostContainers++;
                        var gaps = frames.Zip(frames.Skip(1), (a, b) => (Start: a, Gap: b - a)).Where(p => p.Start < 650).ToArray();
                        Console.WriteLine($"FILTER {theme}/{run}: input={inputMilliseconds:F2}ms, first={frames[0]:F2}ms, frames={frames.Count}, max={gaps.Max(p => p.Gap):F2}ms, over25={string.Join(';', gaps.Where(p => p.Gap > 25).Select(p => $"{p.Start:F0}:{p.Gap:F1}"))}, retained={retained}");
                        if (window.DeviceList.Items.Count != (run % 2 == 0 ? 12 : 18) ||
                            window.SelectedDevice?.IsOnline != (run % 2 == 1) ||
                            !window.DeviceList.IsHitTestVisible || window.DeviceListTransition.IsVisible ||
                            window.DetailPanel.CacheMode is not null)
                            throw new InvalidOperationException("Filter did not settle on the requested list/detail or release its animation state.");
                        // The second physical ListBox must behave like the first after adoption.
                        var selectedRow = (ListBoxItem)window.DeviceList.ItemContainerGenerator.ContainerFromIndex(1);
                        selectedRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                        { RoutedEvent = Mouse.PreviewMouseDownEvent });
                        window.DeviceList.SelectedIndex = 1;
                        Pump(700);
                        if (!ReferenceEquals(window.SelectedDevice, window.DeviceList.SelectedItem))
                            throw new InvalidOperationException("The adopted list lost its selection handler.");
                        var viewer = (ScrollViewer)window.DeviceList.Template.FindName("PART_ScrollViewer", window.DeviceList);
                        viewer.ScrollToBottom(); Pump(80);
                        if (viewer.ScrollableHeight > 0 && viewer.VerticalOffset < 1)
                            throw new InvalidOperationException("The adopted list cannot scroll.");
                    }
                }
                if (!profileOnly && lostContainers != 0)
                    throw new InvalidOperationException($"The entering rows were rebuilt at {lostContainers} transition completions.");
                // A monitoring snapshot arriving mid-animation must be reconciled
                // into the adopted list, including an empty target and a later return.
                window.DeviceFilterSegments.SelectedIndex = 1; Pump(90);
                window.DeviceFilterSegments.SelectedIndex = 0; Pump(90);
                window.DeviceFilterSegments.SelectedIndex = 1; Pump(90);
                typeof(MainWindow).GetMethod("ReplaceDevices", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [devices.Take(18).ToArray(), true, false]);
                Pump(900);
                if (window.DeviceList.Items.Count != 0 || window.SelectedDevice is not null || !window.EmptyPanel.IsVisible)
                    throw new InvalidOperationException("A mid-transition monitoring update left a stale offline device.");
                window.DeviceFilterSegments.SelectedIndex = 0; Pump(900);
                if (window.DeviceList.Items.Count != 18 || window.SelectedDevice?.IsOnline != true || !window.DetailPanel.IsVisible)
                    throw new InvalidOperationException("The list failed to recover from an empty filter after an interrupted transition.");
                Console.WriteLine($"Filter frame regression PASS: 8 transitions, populated charts and history, light/dark; rebuilt={lostContainers}.");
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45))) { Console.Error.WriteLine("Filter frame regression timed out."); Environment.Exit(1); }
        if (failure is null) return 0; Console.Error.WriteLine(failure); return 1;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
