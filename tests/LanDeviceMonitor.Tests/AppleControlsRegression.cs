using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Services;

internal static class AppleControlsRegression
{
    public static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            MainWindow? window = null;
            try
            {
                // App queues its real startup (tray, credentials and single-instance handoff).
                // Cancel that queued work before pumping a dispatcher for visual tests.
                var startupOperations = new List<DispatcherOperation>();
                DispatcherHookEventHandler posted = (_, e) => startupOperations.Add(e.Operation);
                Dispatcher.CurrentDispatcher.Hooks.OperationPosted += posted;
                app = new App();
                Dispatcher.CurrentDispatcher.Hooks.OperationPosted -= posted;
                foreach (var operation in startupOperations) operation.Abort();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeService.Apply(ThemeService.LightMode);
                window = new MainWindow(new NotificationService())
                { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
                // Never initialize monitoring or change the user's persisted settings in a UI test.
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                var devices = new[] { true, false }.Select((online, index) => new LanDeviceMonitor.Models.DeviceViewData
                {
                    Profile = new LanDeviceMonitor.Models.DeviceProfile
                    {
                        MacAddress = $"00:00:00:00:30:0{index + 1}", LastRouterName = online ? "测试笔记本" : "测试手机",
                        LastKnownOnline = online, LastSeenAt = DateTimeOffset.Now, FirstSeenAt = DateTimeOffset.Now,
                        LastIpAddress = $"192.168.31.{100 + index}"
                    },
                    Identity = new LanDeviceMonitor.Models.DeviceIdentity("电脑", "\uE770", online ? "测试笔记本" : "测试手机"),
                    Sessions = []
                }).ToArray();
                typeof(MainWindow).GetMethod("ReplaceDevices", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [devices, false, false]);
                window.Show(); window.UpdateLayout();
                foreach (var pair in new[] { (0, 1), (2, 10), (1, 5), (0, 1), (2, 10) })
                {
                    window.TrafficRangeSegments.SelectedIndex = pair.Item1;
                    Require(window.TrafficRangeMinutes == pair.Item2, "统计时段未同步");
                }
                var segments = window.DeviceFilterSegments;
                segments.SelectedIndex = 1; segments.SelectedIndex = 0; segments.SelectedIndex = 1;
                Pump(300);
                var indicator = (Border)segments.Template.FindName("PART_Indicator", segments);
                Require(Math.Abs(((TranslateTransform)indicator.RenderTransform).X - indicator.ActualWidth) < 1, "滑块没有到达离线分段");
                segments.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Home) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Require(segments.SelectedIndex == 0, "Home 键未返回首段");
                Pump(1200);
                window.Background = new SolidColorBrush(Color.FromRgb(242,243,247));
                Capture(window, "devices-Light.png", ThemeService.LightMode);
                Require(Math.Abs(indicator.CornerRadius.TopLeft - indicator.ActualHeight / 2) < 0.1, "胶囊圆角不是高度的一半");
                typeof(MainWindow).GetMethod("ShowSettingsPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Pump(500);
                window.RouterAddressBox.Text = "10.77.0.1";
                window.DoNotDisturbStartBox.Text = "22:30";
                window.DoNotDisturbEndBox.Text = "08:00";
                window.RefreshIntervalCombo.SelectedIndex = 0;
                window.ThemeModeCombo.SelectedIndex = 0;
                var switchNames = new[] { "StartWithWindowsCheck", "EmergencyHotKeyEnabledCheck", "DoNotDisturbCheck", "NotifyIpv6NonNativeCheck", "LanAccessCheck", "NotifyStatusChangeCheck" };
                foreach (var name in switchNames)
                {
                    var control = window.FindName(name) as SwitchControl;
                    Require(control is not null, name + " 未转换为开关");
                    Require(!string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(control!)), name + " 缺少辅助名称");
                }
                var toggle = window.DoNotDisturbCheck;
                var peer = UIElementAutomationPeer.CreatePeerForElement(toggle)!;
                var provider = (IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!;
                toggle.IsChecked = false;
                provider.Toggle(); Pump(250);
                Require(toggle.IsChecked == true && window.DoNotDisturbStartBox.IsEnabled, "开关未启用时间编辑");
                var knob = (Border)toggle.Template.FindName("PART_Knob", toggle);
                Require(Math.Abs(((TranslateTransform)knob.RenderTransform).X - 20) < 0.1, "开关滑块位置不正确");
                provider.Toggle(); Pump(250);
                Require(toggle.IsChecked == false && !window.DoNotDisturbStartBox.IsEnabled, "关闭后时间编辑未禁用");
                Require(window.FindName("LanQrImage") is null, "二维码仍在设置页中");
                window.StartWithWindowsCheck.IsChecked = true;
                window.EmergencyHotKeyEnabledCheck.IsChecked = true;
                window.NotifyIpv6NonNativeCheck.IsChecked = true;
                window.LanAccessCheck.IsChecked = true;
                window.LanAccessDetails.Visibility = Visibility.Visible;
                window.LanAddressBox.Text = "http://10.77.0.100:17863/";
                window.WebAccessStatus.Text = "已开启 · 同一局域网内打开下方链接即可查看。";
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                foreach (var size in new[] { (1180d,760d), (980d,650d) })
                {
                    ThemeService.Apply(mode);
                    window.Background = new SolidColorBrush(mode == ThemeService.DarkMode ? Color.FromRgb(30,32,38) : Color.FromRgb(242,243,247));
                    window.Width = size.Item1; window.Height = size.Item2;
                    window.SettingsPanel.ScrollToTop(); Pump(250);
                    Capture(window, $"settings-{mode}-{size.Item1}-top.png", mode);
                    window.SettingsPanel.ScrollToBottom(); Pump(250);
                    Capture(window, $"settings-{mode}-{size.Item1}-bottom.png", mode);
                    Require(window.SettingsPanel.ExtentWidth <= window.SettingsPanel.ViewportWidth + 1, "设置页发生横向溢出");
                    Require(window.LanAddressBox.ActualWidth > 180, "局域网链接区域被挤压");
                }
                Console.WriteLine("Apple controls regression PASS: direct ranges, rapid segments, keyboard, six switches, UIA toggle, dependent fields, no QR, light/dark 1180/980 layouts.");
            }
            catch (Exception e) { failure = e; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is null) return 0;
        Console.Error.WriteLine(failure); return 1;
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }


    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static void Capture(MainWindow window, string name, string mode)
    {
        window.UpdateLayout();
        var width = (int)window.ActualWidth; var height = (int)window.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(mode == ThemeService.DarkMode ? Color.FromRgb(30,32,38) : Color.FromRgb(242,243,247)), null, new Rect(0,0,width,height));
            dc.DrawRectangle(new VisualBrush(window), null, new Rect(0,0,width,height));
        }
        var bitmap = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(Environment.CurrentDirectory, ".tools", "ui-dev198");
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory,name)); encoder.Save(file);
    }
}
