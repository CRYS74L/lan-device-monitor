using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;

internal static class HistoryRowLayoutRegression
{
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
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, Width = 500, Height = 1000, ShowActivated = false, ShowInTaskbar = false };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                var now = DateTimeOffset.Now;
                var device = new DeviceViewData
                {
                    Profile = new DeviceProfile { MacAddress = "02:00:00:00:68:01", LastRouterName = "历史布局测试", LastKnownOnline = true, FirstSeenAt = now.AddDays(-400), LastSeenAt = now },
                    Identity = new DeviceIdentity("手机", "\uE8EA", "历史布局测试"),
                    Sessions = new[]
                    {
                        new DeviceSession { MacAddress = "02:00:00:00:68:01", OnlineAt = now.AddMinutes(-103) },
                        new DeviceSession { MacAddress = "02:00:00:00:68:01", OnlineAt = now.AddHours(-3), OfflineAt = now.AddHours(-3) },
                        new DeviceSession { MacAddress = "02:00:00:00:68:01", OnlineAt = now.AddYears(-1).AddDays(-123), OfflineAt = now.AddDays(-3) },
                        new DeviceSession { MacAddress = "02:00:00:00:68:01", OnlineAt = now.AddDays(-500), OfflineAt = now.AddDays(-450) }
                    }.Concat(Enumerable.Range(0, 42).Select(i => new DeviceSession
                    {
                        MacAddress = "02:00:00:00:68:01",
                        OnlineAt = now.AddDays(-600 - i * 130),
                        OfflineAt = now.AddDays(-600 - i * 130).AddHours(i % 2 == 0 ? 2 : 2999)
                    })).ToArray()
                };
                typeof(MainWindow).GetMethod("ReplaceDevices", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new[] { device }, false, false]);
                window.Show(); Pump();
                var deepChecked = false;
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                foreach (var width in new[] { 500d, 380d, 600d, 1180d, 500d })
                {
                    ThemeService.Apply(mode); window.Width = width; Pump();
                    typeof(MainWindow).GetMethod("ShowDevicesPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false, false]); Pump(700);
                    window.SessionHistoryTitleText.BringIntoView(); Pump();
                    var badges = Descendants<Border>(window.SessionHistoryItems).Where(b => b.Style == app.FindResource("TimelineDurationBadgeStyle")).ToArray();
                    Require(badges.Length >= 3, "History fixture did not realize three rows.");
                    foreach (var badge in badges.Take(3))
                    {
                        var row = (Panel)VisualTreeHelper.GetParent(badge);
                        var details = (FrameworkElement)row.Children[1];
                        details.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        var needed = 24 + details.DesiredSize.Width + badge.DesiredSize.Width - badge.Margin.Left - badge.Margin.Right + 12 + 2;
                        if (needed <= row.ActualWidth)
                        {
                            var center = badge.TranslatePoint(new Point(0, badge.ActualHeight / 2), details).Y;
                            Require(center >= 0 && center <= details.ActualHeight,
                                $"Duration stacked despite fitting: window={width}, row={row.ActualWidth}, needed={needed}.");
                        }
                        if (width == 500 && ReferenceEquals(badge, badges[0]))
                            Require(badge.TranslatePoint(new Point(0, badge.ActualHeight / 2), details).Y <= details.ActualHeight,
                                $"Screenshot-sized first row unexpectedly stacked: actual={row.ActualWidth}, needed={needed}.");
                        var card = (FrameworkElement)VisualTreeHelper.GetParent(row);
                        Require(badge.TranslatePoint(new Point(0, badge.ActualHeight), card).Y <= card.ActualHeight - 9,
                            "Duration is clipped by an underestimated history row height.");
                    }
                    Require(ToolTipService.GetToolTip(window.OlderHistoryToggle) is null, "History action still has a hover tooltip.");
                    Require(window.OlderHistoryToggle.BorderBrush == app.FindResource("TimelineItemBorderBrush") && window.OlderHistoryToggle.BorderThickness == new Thickness(1), "History action outline differs from its adjacent record card.");
                    for (var i = 1; i < badges.Take(3).Count(); i++)
                    {
                        var previous = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(badges[i - 1]));
                        var current = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(badges[i]));
                        var previousBottom = previous.TranslatePoint(new Point(0, previous.ActualHeight), window.SessionHistoryItems).Y;
                        Require(current.TranslatePoint(new Point(), window.SessionHistoryItems).Y >= previousBottom - .5, "Mixed history rows overlap.");
                    }
                    var lastCard = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(badges[2]));
                    Require(window.SessionHistoryDisclosure.ActualHeight >= lastCard.TranslatePoint(new Point(0, lastCard.ActualHeight), window.SessionHistoryItems).Y - 1,
                        "Collapsed reveal clips its third record.");
                    if (width == 500) Capture(window.SessionHistoryCard, mode);
                    if (width == 500 && !deepChecked) { VerifyDeepHistory(window); deepChecked = true; }
                }
                Console.WriteLine("History layout PASS: fitting durations stay alongside dates; light/dark 380/500/600/1180, mixed date lengths, outline and no hover tooltip.");
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45))) { Console.Error.WriteLine("History layout regression timed out."); Environment.Exit(1); }
        if (failure is null) return 0; Console.Error.WriteLine(failure); return 1;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var item in Descendants<T>(child)) yield return item; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Capture(FrameworkElement card, string mode)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, card.ActualWidth, card.ActualHeight);
            dc.DrawRectangle(Brushes.White, null, bounds); dc.DrawRectangle(new VisualBrush(card), null, bounds);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth * 1.25), (int)Math.Ceiling(card.ActualHeight * 1.25), 120, 120, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var folder = System.IO.Path.Combine(Environment.CurrentDirectory, "runtime-data", "history-layout-dev268");
        System.IO.Directory.CreateDirectory(folder);
        using var file = System.IO.File.Create(System.IO.Path.Combine(folder, $"history-500-{mode}.png")); encoder.Save(file);
    }
    private static void VerifyDeepHistory(MainWindow window)
    {
        while (window.HasOlderSessions) { window.OlderHistoryToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(500); }
        var panel = Descendants<LanDeviceMonitor.Controls.SessionHistoryPanel>(window.SessionHistoryItems).Single();
        foreach (var index in new[] { 20, 40 })
        {
            for (var pass = 0; pass < 3; pass++)
            {
                var origin = panel.TranslatePoint(new Point(), window.DetailPanel).Y + window.DetailPanel.VerticalOffset;
                window.DetailPanel.ScrollToVerticalOffset(origin + panel.HeightForCount(index)); Pump();
            }
            Require(window.SessionHistoryItems.ItemContainerGenerator.ContainerFromIndex(index) is not null, "Mixed-height middle record was not realized.");
        }
        foreach (var width in new[] { 380d, 600d, 500d })
        {
            window.Width = width; Pump(700); window.DetailPanel.ScrollToBottom(); Pump();
            Require(window.SessionHistoryItems.ItemContainerGenerator.ContainerFromIndex(window.HistorySessionCount - 1) is not null, "Mixed-height last record was not reachable after resizing.");
            var cards = Descendants<LanDeviceMonitor.Controls.SessionHistoryRow>(window.SessionHistoryItems)
                .Select(row => (FrameworkElement)VisualTreeHelper.GetParent(row)).OrderBy(card => card.TranslatePoint(new Point(), panel).Y).ToArray();
            for (var i = 1; i < cards.Length; i++)
                Require(cards[i].TranslatePoint(new Point(), panel).Y >= cards[i - 1].TranslatePoint(new Point(0, cards[i - 1].ActualHeight), panel).Y - .5, "Deep mixed-height rows overlap.");
            Require(window.OlderHistoryToggle.TranslatePoint(new Point(), window.SessionHistoryCard).Y >= window.SessionHistoryDisclosure.TranslatePoint(new Point(0, window.SessionHistoryDisclosure.ActualHeight), window.SessionHistoryCard).Y,
                "History action overlaps the last revealed record.");
        }
        window.OlderHistoryToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(500);
        Require(window.VisibleHistoryCount == 3, "Deep history did not collapse back to three records.");
        window.SessionHistoryTitleText.BringIntoView(); Pump();
        Console.WriteLine("Mixed-height history PASS: 46 records, incremental reveal, middle/end virtualization, resize and collapse.");
    }
    private static void Pump(int milliseconds = 0)
    {
        var frame = new DispatcherFrame();
        if (milliseconds == 0) Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        else
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start();
        }
        Dispatcher.PushFrame(frame);
    }
}
