using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;
using Point = System.Windows.Point;

internal static class PortraitCaptionBackRegression
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly DependencyPropertyKey HoverKey = (DependencyPropertyKey)typeof(UIElement)
        .GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    private static readonly DependencyPropertyKey PressedKey = (DependencyPropertyKey)typeof(ButtonBase)
        .GetField("IsPressedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            MainWindow? window = null;
            try
            {
                app = TestAppFactory.Create(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window = new MainWindow(new NotificationService())
                {
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
                    Width = 430, Height = 760, ShowActivated = false, ShowInTaskbar = false
                };
                typeof(MainWindow).GetField("_isPopulatingAppSettings", PrivateInstance)!.SetValue(window, true);
                var now = DateTimeOffset.Now;
                var fixture = new DeviceViewData
                {
                    Profile = new DeviceProfile
                    {
                        MacAddress = "02:00:00:00:73:01", LastRouterName = "返回导航测试设备",
                        LastKnownOnline = true, LastSeenAt = now, FirstSeenAt = now.AddHours(-2)
                    },
                    Identity = new DeviceIdentity("手机", "\uE8EA", "返回导航测试设备"), Sessions = []
                };
                Call(window, "ReplaceDevices", new[] { fixture }, false, false);
                window.Show(); window.UpdateLayout(); Pump(250);
                var back = window.FindName("CaptionBackButton") as Button;
                Require(back is not null, "竖屏详情缺少顶栏箭头＋返回按钮。");
                Require(WindowChrome.GetIsHitTestVisibleInChrome(back!), "顶栏返回键没有接入 WindowChrome 命中区域。");
                VerifyCaptionHeight(window);
                Require(!back!.IsVisible && !window.CompactBackButton.IsVisible, "设备列表不应显示可用的返回键。");

                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    Call(window, "ApplyTheme", mode, false);
                    foreach (var page in new[] { "ShowDevicesPanel", "ShowSettingsPanel", "ShowRouterPanel" })
                    {
                        if (page == "ShowDevicesPanel") Call(window, page, false, false); else Call(window, page);
                        Pump(950); window.UpdateLayout();
                        Require(back.IsVisible && back.IsEnabled && back.IsHitTestVisible,
                            $"竖屏 {mode}/{page} 没有可用的顶栏返回键。");
                        Require(!window.CompactBackButton.IsVisible, "竖屏正文仍显示重复圆形返回键。");
                        var origin = back.TranslatePoint(new Point(), window.WindowRoot);
                        Require(origin.X <= 8 && origin.Y >= 0 && origin.Y + back.ActualHeight <= 32.01,
                            $"返回键不在原有顶栏左上角：{origin}, height={back.ActualHeight}。");
                        Require(Descendants<TextBlock>(back).Any(text => text.Text == "返回") && Descendants<System.Windows.Shapes.Path>(back).Any(),
                            "顶栏返回键缺少向左箭头或返回文字。");
                        VerifyCaptionHeight(window);
                        VerifyNativeCaptionHit(window, back);
                        Capture(window, $"portrait-{mode}-{page}.png");

                        var selected = window.SelectedDevice;
                        back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Require(!back.IsEnabled && !back.IsHitTestVisible, "返回离场动画期间仍允许重复激活。");
                        Pump(950);
                        Require(window.DeviceSidebar.IsVisible && !window.DetailSurface.IsVisible && !back.IsVisible,
                            $"{page} 的顶栏返回点击没有真正回到设备列表。");
                        Require(ReferenceEquals(selected, window.SelectedDevice), "返回列表错误地清除了已选设备。");
                    }
                }

                Call(window, "ShowSettingsPanel"); Pump(950);
                back.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Enter)
                    { RoutedEvent = Keyboard.KeyDownEvent });
                Pump(950);
                Require(window.DeviceSidebar.IsVisible && !window.DetailSurface.IsVisible && !back.IsVisible,
                    "顶栏返回键的真实 Enter 路由没有回到设备列表。");
                Console.WriteLine("Caption keyboard return PASS: routed Enter input returned settings to the device list.");

                Call(window, "ShowSettingsPanel"); Pump(950);
                window.Width = 760; window.Height = 600; window.UpdateLayout(); Pump(300);
                Require(!back.IsVisible && window.CompactBackButton.IsVisible, "横屏紧凑布局没有恢复原正文圆形返回键。");
                Capture(window, "landscape-compact-settings.png");
                window.Height = 1000; window.UpdateLayout(); Pump(300);
                Require(back.IsVisible && !window.CompactBackButton.IsVisible,
                    "只改变高度进入竖屏时没有把返回键移到顶栏。");
                VerifyCaptionHeight(window);
                Capture(window, "portrait-height-only-settings.png");
                window.Height = window.WorkspaceGrid.ActualWidth + 32; window.UpdateLayout(); Pump(250);
                Require(Math.Abs(window.WorkspaceGrid.ActualHeight - window.WorkspaceGrid.ActualWidth) < .01 &&
                    back.IsVisible && !window.CompactBackButton.IsVisible,
                    "页面高宽相等时没有使用包含等号的竖屏返回布局。");
                window.Height -= 1; window.UpdateLayout(); Pump(250);
                Require(!back.IsVisible && window.CompactBackButton.IsVisible,
                    "页面高度小于宽度 1 DIP 时没有退出竖屏返回布局。");
                window.Height = 600; window.UpdateLayout(); Pump(300);
                Require(!back.IsVisible && window.CompactBackButton.IsVisible,
                    "只改变高度退出竖屏时没有恢复正文返回键。");
                window.CompactBackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(950);
                Require(window.DeviceSidebar.IsVisible && !window.DetailSurface.IsVisible, "保留的横屏圆形返回键失效。");

                window.Width = 980; window.Height = 1100; window.UpdateLayout(); Pump(300);
                Call(window, "ShowRouterPanel"); Pump(300);
                Require(!back.IsVisible && !window.CompactBackButton.IsVisible, "宽屏双栏布局误显示紧凑导航返回键。");
                window.Width = 430; window.Height = 760; window.UpdateLayout(); Pump(300);
                Require(!back.IsVisible && window.DeviceSidebar.IsVisible, "跨越紧凑布局断点后残留详情返回态。");
                VerifyCaptionHeight(window);

                Call(window, "ShowDevicesPanel", false, false); Pump(950);
                VerifyInteractionMotion(back, window);
                VerifyPlacementReversal(window, back);
                Console.WriteLine("Portrait caption back PASS: real details/settings/router return, light/dark, 32 DIP caption/chrome, width and height-only switches, hover/press/reversal and nonlinear WPF clocks.");
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(40))) { Console.Error.WriteLine("Portrait caption back regression timed out."); Environment.Exit(1); }
        if (failure is null) return 0;
        Console.Error.WriteLine(failure); return 1;
    }

    private static void VerifyInteractionMotion(Button back, MainWindow window)
    {
        back.ApplyTemplate();
        var hover = back.Template.FindName("CaptionBackHover", back) as Border;
        var root = back.Template.FindName("CaptionBackMotionRoot", back) as FrameworkElement;
        Require(hover is not null && root?.RenderTransform is ScaleTransform, "顶栏返回键缺少独立悬停/按下动画表面。");
        var scale = (ScaleTransform)root!.RenderTransform;
        SetHover(back, true);
        if (SystemParameters.ClientAreaAnimation)
        {
            Pump(35);
            VerifyNonlinearClock(hover!, UIElement.OpacityProperty, 0, 1, "hover");
            var before = hover!.Opacity;
            Require(before > 0 && before < 1, "悬停缺少呈现中的中间值。");
            SetHover(back, false);
            Require(Math.Abs(hover.Opacity - before) < .0001, "悬停中途离开时没有从当前呈现值反转。");
            Pump(35); Require(hover.Opacity < before, "悬停反向动画没有离开当前值。");
            before = hover.Opacity; SetHover(back, true);
            Require(Math.Abs(hover.Opacity - before) < .0001, "连续悬停反转出现跳变。");
        }
        Pump(240); Require(Math.Abs(hover!.Opacity - 1) < .001, "悬停动画没有到达目标。");
        Capture(back, "caption-back-hover.png", 3);
        back.SetValue(PressedKey, true);
        if (SystemParameters.ClientAreaAnimation)
        {
            Pump(30);
            VerifyNonlinearClock(scale, ScaleTransform.ScaleYProperty, 1, .97, "press");
            var before = scale.ScaleY;
            Require(before > .97 && before < 1, "按下缺少缩放中间值。");
            back.SetValue(PressedKey, false);
            Require(Math.Abs(scale.ScaleY - before) < .0001, "按下中途松开没有从当前缩放反转。");
            Pump(25); Require(scale.ScaleY > before, "松开动画没有向正常大小返回。");
            before = scale.ScaleY; back.SetValue(PressedKey, true);
            Require(Math.Abs(scale.ScaleY - before) < .0001, "快速再次按下发生跳变。");
        }
        Pump(160);
        Require(Math.Abs(scale.ScaleX - .97) < .001 && Math.Abs(scale.ScaleY - .97) < .001 && Math.Abs(hover.Opacity - .82) < .001,
            "按下的独立缩放/表面反馈未到达目标。");
        Capture(back, "caption-back-pressed.png", 3);
        back.SetValue(PressedKey, false); SetHover(back, false); Pump(260);
        Require(Math.Abs(scale.ScaleX - 1) < .001 && Math.Abs(scale.ScaleY - 1) < .001 && hover.Opacity < .001,
            "交互结束后悬停或按下状态残留。");
        Require(!scale.HasAnimatedProperties && !hover.HasAnimatedProperties,
            "交互完成后未释放动画时钟。");
        Capture(window, "portrait-final-detail.png");
    }

    private static void VerifyPlacementReversal(MainWindow window, Button back)
    {
        if (!SystemParameters.ClientAreaAnimation) { Console.WriteLine("Animation disabled by system: final state checks only."); return; }
        window.Height = 600; window.Width = 760; window.UpdateLayout(); Pump(250);
        window.Height = 1000; window.UpdateLayout(); Pump(35);
        VerifyNonlinearClock(back, UIElement.OpacityProperty, 0, 1, "placement entrance");
        var before = back.Opacity;
        Require(before > 0 && before < 1, "顶栏出现动画没有中间值。");
        window.Height = 600; window.UpdateLayout();
        Require(Math.Abs(back.Opacity - before) < .0001, "高度切换的离场动画没有从当前透明度反转。");
        Require(!back.IsEnabled && !back.IsHitTestVisible, "离场的顶栏返回键仍可交互。");
        Pump(30); Require(back.Opacity < before, "顶栏离场反向动画未推进。");
        before = back.Opacity; window.Height = 1000; window.UpdateLayout();
        Require(Math.Abs(back.Opacity - before) < .0001 && back.IsEnabled,
            "高度快速反向切换出现跳变或过期完成回调。");
        Pump(300);
        Require(back.IsVisible && Math.Abs(back.Opacity - 1) < .001 && !window.CompactBackButton.IsVisible,
            "快速高度反向切换没有停在最终竖屏状态。");
        Require(!back.HasAnimatedProperties && !back.RenderTransform.HasAnimatedProperties,
            "导航状态切换完成后未释放时钟。");
        VerifyCaptionHeight(window);
    }

    private static void VerifyNonlinearClock(DependencyObject target, DependencyProperty property, double from, double to, string label)
    {
        // Inspect the clock WPF actually applies, then compare its rendered
        // value to linear progress at that same clock time (no wall-clock guess).
        var storageType = typeof(AnimationClock).Assembly.GetType("System.Windows.Media.Animation.AnimationStorage")!;
        var storage = storageType.GetMethod("GetStorage", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(null, new object[] { target, property });
        Require(storage is not null, $"{label} 没有 WPF 动画存储。");
        var clocks = storageType.GetField("_animationClocks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(storage)!;
        var count = (int)clocks.GetType().GetProperty("Count")!.GetValue(clocks)!;
        var clock = (AnimationClock)clocks.GetType().GetProperty("Item")!.GetValue(clocks, new object[] { count - 1 })!;
        var progress = clock.CurrentProgress ?? 0;
        var actual = (double)target.GetValue(property);
        var linear = from + (to - from) * progress;
        Require(progress > .01 && progress < .9 && Math.Abs(actual - linear) > Math.Abs(to - from) * .05,
            $"{label} 没有非线性呈现证据：progress={progress:F4}, actual={actual:F4}, linear={linear:F4}。");
        Console.WriteLine($"Nonlinear WPF clock {label}: progress={progress:F4}, actual={actual:F4}, linear={linear:F4}.");
    }

    private static void VerifyCaptionHeight(MainWindow window)
    {
        Require(Math.Abs(window.WindowRoot.RowDefinitions[0].ActualHeight - 32) < .001 &&
            Math.Abs(WindowChrome.GetWindowChrome(window).CaptionHeight - 32) < .001 &&
            Math.Abs(window.WorkspaceGrid.TranslatePoint(new Point(), window.WindowRoot).Y - 32) < .001,
            "返回键改变了顶栏高度、WindowChrome 高度或正文起点。");
    }

    private static void SetHover(UIElement element, bool value)
    {
        // UIElement's public getter uses the reverse-inheritance cache bit,
        // whereas templates observe the read-only DP. Feed the same two WPF
        // states as mouse input, without moving the user's physical pointer.
        var writeFlag = typeof(UIElement).GetMethod("WriteFlag", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var cacheFlag = Enum.Parse(writeFlag.GetParameters()[0].ParameterType, "IsMouseOverCache");
        writeFlag.Invoke(element, new[] { cacheFlag, (object)value });
        element.SetValue(HoverKey, value);
        Require(element.IsMouseOver == value, "WPF 悬停输入没有更新框架缓存。");
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private static void VerifyNativeCaptionHit(MainWindow window, Button back)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        int Hit(Point screen)
        {
            var packed = unchecked((int)(((uint)(int)screen.Y & 0xffff) << 16 | ((uint)(int)screen.X & 0xffff)));
            return SendMessage(handle, 0x84, IntPtr.Zero, new IntPtr(packed)).ToInt32();
        }
        Require(Hit(back.PointToScreen(new Point(back.ActualWidth / 2, back.ActualHeight / 2))) == 1,
            "返回键中心仍被原生顶栏当作拖动区。");
        Require(Hit(window.WindowRoot.PointToScreen(new Point(120, 20))) == 2,
            "新增返回键破坏了旁边的原生顶栏拖动区。");
        Require(Hit(window.WindowRoot.PointToScreen(new Point(window.WindowRoot.ActualWidth / 2, 2))) == 12,
            "新增返回键破坏了顶栏上边缘的缩放区。");
    }

    private static void Call(MainWindow window, string method, params object[] arguments) =>
        typeof(MainWindow).GetMethod(method, PrivateInstance)!.Invoke(window, arguments);

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T result) yield return result;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Capture(FrameworkElement element, string name, int scale = 1)
    {
        element.UpdateLayout();
        var output = Environment.GetEnvironmentVariable("LAN_CAPTION_BACK_EVIDENCE_DIR") ??
            System.IO.Path.Combine(Environment.CurrentDirectory, "runtime-data", "portrait-caption-back");
        System.IO.Directory.CreateDirectory(output);
        // Offscreen WPF exports can reuse a transform's cached rendering
        // resource. Snapshot its presented value into a fresh resource for
        // the screenshot, then restore the live transform after the export.
        var transforms = Descendants<UIElement>(element).Prepend(element)
            .Where(item => !item.RenderTransform.Value.IsIdentity)
            .Select(item => (Element: item, Transform: item.RenderTransform)).ToArray();
        foreach (var pair in transforms) pair.Element.RenderTransform = pair.Transform.CloneCurrentValue();
        element.UpdateLayout();
        var width = element.ActualWidth; var height = element.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(ThemeService.ResolveIsDark(ThemeService.CurrentMode)
                ? Color.FromRgb(30, 32, 38) : Color.FromRgb(242, 243, 247)), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(element)
            {
                ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, width, height)
            }, null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(System.IO.Path.Combine(output, name)); encoder.Save(stream);
        foreach (var pair in transforms) pair.Element.RenderTransform = pair.Transform;
        element.UpdateLayout();
    }
}
