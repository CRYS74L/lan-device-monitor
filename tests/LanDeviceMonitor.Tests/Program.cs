using LanDeviceMonitor;
using LanDeviceMonitor.Services;
using LanDeviceMonitor.ViewModels;
using System.Reflection;

if (args.Contains("--startup-smoke", StringComparer.OrdinalIgnoreCase))
{
    return RunStartupSmoke();
}

var failures = new List<string>();
var checksRun = 0;

Check(
    DeviceIdentityService.Identify("", "yeelink-light-lamp27_mibt053F", 1).TypeName == "灯",
    "Yeelight 型号应识别为灯");
Check(
    DeviceIdentityService.Identify("", "cgllc-airm-cgd1st_mibtEC5B", 1).TypeName == "空气检测仪",
    "CGLLC 空气设备应识别为空气检测仪");
Check(
    DeviceIdentityService.Identify("", "MiAiSoundbox-L15A", 1).SuggestedName == "小爱音箱",
    "小米音箱应使用中文建议名称");
Check(
    DeviceIdentityService.Identify("", "Redmi-Note-11T-Pro", 2).TypeName == "手机",
    "Redmi Note 应识别为手机");
Check(DeviceItemViewModel.FormatSpeed(1024) == "1.0 KB/s", "速度单位换算不正确");
Check(DeviceItemViewModel.FormatBytes(1024L * 1024 * 1024) == "1.00 GB", "流量单位换算不正确");
Check(NotificationSchedule.TryParseTime("22:30", out var startMinutes) &&
      startMinutes == 1350,
    "免打扰开始时间解析不正确");
Check(!NotificationSchedule.TryParseTime("25:00", out _),
    "无效的免打扰时间不应被接受");
Check(NotificationSchedule.IsMuted(
        new DateTimeOffset(2026, 7, 24, 23, 0, 0, TimeSpan.FromHours(8)),
        true,
        22 * 60,
        8 * 60),
    "跨零点免打扰时段的夜间部分判断不正确");
Check(NotificationSchedule.IsMuted(
        new DateTimeOffset(2026, 7, 24, 7, 30, 0, TimeSpan.FromHours(8)),
        true,
        22 * 60,
        8 * 60),
    "跨零点免打扰时段的清晨部分判断不正确");
Check(!NotificationSchedule.IsMuted(
        new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.FromHours(8)),
        true,
        22 * 60,
        8 * 60),
    "免打扰时段以外不应静音通知");
Check(
    TrayService.CalculateMenuPosition(
        new System.Drawing.Point(1200, 800),
        new System.Drawing.Rectangle(0, 0, 1920, 1040),
        180,
        90,
        1d) == new System.Drawing.Point(1191, 719),
    "托盘菜单可见区域的左下角应与托盘图标右上角对齐");
Check(
    TrayService.CalculateMenuPosition(
        new System.Drawing.Point(4, 1000),
        new System.Drawing.Rectangle(0, 0, 1920, 1040),
        180,
        90,
        1d).X == 0,
    "托盘菜单不应超出工作区左侧");
Check(
    TrayService.CalculateMenuPosition(
        new System.Drawing.Point(100, 20),
        new System.Drawing.Rectangle(0, 0, 1920, 1040),
        180,
        90,
        1d).Y == 11,
    "顶部空间不足时托盘菜单应显示在右键位置下方");
Check(
    Math.Abs(SmoothScrollBehavior.EaseOutQuadratic(0.25d) - 0.4375d) < 0.0001d &&
    Math.Abs(SmoothScrollBehavior.EaseOutQuadratic(0.5d) - 0.75d) < 0.0001d &&
    Math.Abs(SmoothScrollBehavior.EaseOutQuadratic(0.75d) - 0.9375d) < 0.0001d,
    "滚轮平滑滚动应恢复为上一版的二次非线性缓出曲线");
Check(
    Math.Abs(SmoothScrollBehavior.GetDeviceListWheelDelta(-120) - 80d) < 0.0001d,
    "左侧设备栏单次标准滚轮距离应为 80 DIP");
Check(
    Math.Abs(SmoothScrollBehavior.GetDeviceListWheelDelta(-18) - 12d) < 0.0001d,
    "左侧设备栏应按高精度滚轮输入幅度滚动，不能把小输入放大成整格");
Check(
    Math.Abs(SmoothScrollBehavior.GetContentWheelDelta(-120) - 100d) < 0.0001d,
    "右侧内容板块应使用 100 DIP 的非线性平滑滚动");
Check(
    Math.Abs(SmoothScrollBehavior.GetAnimationDurationMilliseconds() - 400d) < 0.0001d,
    "左右板块平滑滚动动画时长均应为 400 ms");
Check(
    Math.Abs(SmoothScrollBehavior.GetBoundedTargetOffset(100d, 164d, 80d, 500d) - 244d) < 0.0001d,
    "左侧连续滚轮输入的待滚动距离应限制为 160 DIP");
Check(
    Math.Abs(SmoothScrollBehavior.GetBoundedTargetOffset(100d, 164d, -80d, 500d) - 20d) < 0.0001d,
    "反向滚轮输入应立即反向，不能延续上一方向的惯性");
Check(
    MainWindow.GetDisplayVersion() == "v0.1.0-dev.48",
    "设置界面应显示当前实际软件版本");
Check(
    MicaWindowService.GetAcrylicGradientColor(useDarkMode: false) ==
        unchecked((int)0x14F5F5F5) &&
    MicaWindowService.GetAcrylicGradientColor(useDarkMode: true) ==
        unchecked((int)0x30202020),
    "浅色和深色模式应使用更透明、低饱和度的中性 Acrylic 染色");
Exception? trayMenuOpenException = null;
var trayMenuOpenInvoked = false;
var verticalScrollBarVisible = false;
var verticalScrollBarWidth = 0d;
var verticalScrollThumbVisible = false;
var verticalScrollBarMaximum = 0d;
var verticalScrollBarViewport = 0d;
var verticalScrollThumbHeight = 0d;
var verticalScrollThumbMinimumHeight = 0d;
var scrollThumbDragAnimationConfigured = false;
var scrollThumbHoverAnimationConfigured = false;
var deviceListScrollBarVisible = false;
var settingsScrollBarVisible = false;
var deviceListScrollThumbHeight = 0d;
var settingsScrollThumbHeight = 0d;
var deviceListHoverAnimationConfigured = false;
var trayMenuHoverAnimationConfigured = false;
var trayOpenIconTextAligned = false;
var acrylicAccentApplied = false;
var themePaletteConfigured = false;
var detailIconThemeConfigured = false;
var visualQaImagePath = Environment.GetEnvironmentVariable(
    "LAN_DEVICE_MONITOR_QA_IMAGE");
var darkVisualQaImagePath = Environment.GetEnvironmentVariable(
    "LAN_DEVICE_MONITOR_DARK_QA_IMAGE");
var trayVisualQaImagePath = Environment.GetEnvironmentVariable(
    "LAN_DEVICE_MONITOR_TRAY_QA_IMAGE");
var trayMenuThread = new Thread(() =>
{
    try
    {
        var application = new LanDeviceMonitor.App();
        application.InitializeComponent();
        ThemeService.Apply(ThemeService.LightMode);
        var lightScrollDefault =
            (System.Windows.Media.Color)application.Resources["ScrollThumbColor"];
        var lightScrollHover =
            (System.Windows.Media.Color)application.Resources["ScrollThumbHoverColor"];
        var lightCombo =
            (System.Windows.Media.Color)application.Resources["ComboBoxColor"];
        var lightControl =
            (System.Windows.Media.Color)application.Resources["ControlColor"];
        var lightPopup =
            (System.Windows.Media.Color)application.Resources["ComboBoxPopupColor"];
        var lightPanel =
            (System.Windows.Media.Color)application.Resources["MicaPanelColor"];
        var lightCard =
            (System.Windows.Media.Color)application.Resources["MicaCardColor"];
        var lightTimelineItem =
            (System.Windows.Media.Color)application.Resources["TimelineItemColor"];
        var lightOnlineDot =
            (System.Windows.Media.Color)application.Resources["TimelineOnlineDotColor"];
        var lightOfflineDot =
            (System.Windows.Media.Color)application.Resources["TimelineOfflineDotColor"];
        ThemeService.Apply(ThemeService.DarkMode);
        var darkScrollDefault =
            (System.Windows.Media.Color)application.Resources["ScrollThumbColor"];
        var darkScrollHover =
            (System.Windows.Media.Color)application.Resources["ScrollThumbHoverColor"];
        var darkCombo =
            (System.Windows.Media.Color)application.Resources["ComboBoxColor"];
        var darkControl =
            (System.Windows.Media.Color)application.Resources["ControlColor"];
        var darkPopup =
            (System.Windows.Media.Color)application.Resources["ComboBoxPopupColor"];
        var darkPanel =
            (System.Windows.Media.Color)application.Resources["MicaPanelColor"];
        var darkTimelineItem =
            (System.Windows.Media.Color)application.Resources["TimelineItemColor"];
        var darkTimelineDuration =
            (System.Windows.Media.Color)application.Resources["TimelineDurationColor"];
        var darkOnlineDot =
            (System.Windows.Media.Color)application.Resources["TimelineOnlineDotColor"];
        var darkOfflineDot =
            (System.Windows.Media.Color)application.Resources["TimelineOfflineDotColor"];
        themePaletteConfigured =
            lightScrollDefault == System.Windows.Media.Color.FromArgb(0x88, 0x66, 0x66, 0x66) &&
            lightScrollHover == System.Windows.Media.Color.FromArgb(0x96, 0x66, 0x66, 0x66) &&
            darkScrollDefault == System.Windows.Media.Color.FromArgb(0x96, 0xC2, 0xC2, 0xC2) &&
            darkScrollHover == System.Windows.Media.Color.FromArgb(0xA5, 0xC2, 0xC2, 0xC2) &&
            lightScrollHover.A == (byte)Math.Round(lightScrollDefault.A * 1.1d) &&
            darkScrollHover.A == (byte)Math.Round(darkScrollDefault.A * 1.1d) &&
            lightCombo == lightControl &&
            lightPopup.A == 0xE6 &&
            darkCombo == darkControl &&
            darkPopup.A == 0xE6 &&
            lightPanel == System.Windows.Media.Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF) &&
            lightCard == System.Windows.Media.Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF) &&
            darkPanel == System.Windows.Media.Color.FromArgb(0x0C, 0x20, 0x20, 0x20) &&
            lightTimelineItem == System.Windows.Media.Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF) &&
            lightOnlineDot == System.Windows.Media.Color.FromRgb(0x68, 0xD9, 0xA3) &&
            lightOfflineDot == System.Windows.Media.Color.FromRgb(0xFF, 0x85, 0x85) &&
            darkOnlineDot == System.Windows.Media.Color.FromRgb(0x6C, 0xEF, 0xAE) &&
            darkOfflineDot == System.Windows.Media.Color.FromRgb(0xFF, 0x9B, 0x9B) &&
            darkTimelineItem == System.Windows.Media.Color.FromArgb(0x8A, 0x2B, 0x2B, 0x2B) &&
            darkTimelineDuration == System.Windows.Media.Color.FromArgb(0xB0, 0x3A, 0x3A, 0x3A);
        ThemeService.Apply(ThemeService.LightMode);
        var overflowViewer = new System.Windows.Controls.ScrollViewer
        {
            Style = (System.Windows.Style)application.FindResource(
                "WindowsSettingsScrollViewerStyle"),
            Width = 220,
            Height = 120,
            VerticalScrollBarVisibility =
                System.Windows.Controls.ScrollBarVisibility.Auto,
            Content = new System.Windows.Controls.Border
            {
                Width = 180,
                Height = 600
            }
        };
        var scrollbarTestWindow = new System.Windows.Window
        {
            Width = 240,
            Height = 140,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = System.Windows.WindowStyle.None,
            Content = overflowViewer
        };
        scrollbarTestWindow.Show();
        scrollbarTestWindow.UpdateLayout();
        var verticalScrollBar =
            FindVisualDescendant<System.Windows.Controls.Primitives.ScrollBar>(
                overflowViewer,
                scrollBar =>
                    scrollBar.Orientation ==
                    System.Windows.Controls.Orientation.Vertical);
        var verticalThumb =
            FindVisualDescendant<System.Windows.Controls.Primitives.Thumb>(
                verticalScrollBar);
        verticalScrollBarVisible =
            verticalScrollBar?.Visibility == System.Windows.Visibility.Visible;
        verticalScrollBarWidth = verticalScrollBar?.ActualWidth ?? 0d;
        verticalScrollBarMaximum = verticalScrollBar?.Maximum ?? 0d;
        verticalScrollBarViewport = verticalScrollBar?.ViewportSize ?? 0d;
        verticalScrollThumbHeight = verticalThumb?.ActualHeight ?? 0d;
        verticalScrollThumbMinimumHeight = verticalThumb?.MinHeight ?? 0d;
        verticalScrollThumbVisible =
            verticalThumb?.Visibility == System.Windows.Visibility.Visible &&
            verticalThumb.MinHeight >= 28d;
        var draggingTrigger = verticalThumb?.Template.Triggers
            .OfType<System.Windows.Trigger>()
            .FirstOrDefault(trigger =>
                trigger.Property ==
                    System.Windows.Controls.Primitives.Thumb.IsDraggingProperty &&
                Equals(trigger.Value, true));
        var dragEnterAnimation = draggingTrigger?.EnterActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        var dragExitAnimation = draggingTrigger?.ExitActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        scrollThumbDragAnimationConfigured =
            dragEnterAnimation?.Duration.HasTimeSpan == true &&
            dragEnterAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(300) &&
            dragEnterAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode:
                        System.Windows.Media.Animation.EasingMode.EaseIn
                } &&
            dragExitAnimation?.Duration.HasTimeSpan == true &&
            dragExitAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(300) &&
            dragExitAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode:
                        System.Windows.Media.Animation.EasingMode.EaseOut
                };
        var hoverTrigger = verticalThumb?.Template.Triggers
            .OfType<System.Windows.Trigger>()
            .FirstOrDefault(trigger =>
                trigger.Property == System.Windows.UIElement.IsMouseOverProperty &&
                Equals(trigger.Value, true));
        var hoverEnterAnimation = hoverTrigger?.EnterActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        var hoverExitAnimation = hoverTrigger?.ExitActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        scrollThumbHoverAnimationConfigured =
            hoverEnterAnimation?.Duration.HasTimeSpan == true &&
            hoverEnterAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(300) &&
            hoverEnterAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase &&
            hoverExitAnimation?.Duration.HasTimeSpan == true &&
            hoverExitAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(300) &&
            hoverExitAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase;
        scrollbarTestWindow.Close();

        var smokeWindow = new MainWindow(new NotificationService())
        {
            Left = -10000,
            Top = -10000,
            Width = 900,
            Height = 560,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        var testDevices = Enumerable.Range(1, 20)
            .Select(index => new DeviceItemViewModel(new LanDeviceMonitor.Models.DeviceViewData
            {
                Profile = new LanDeviceMonitor.Models.DeviceProfile
                {
                    MacAddress = $"00:00:00:00:00:{index:00}",
                    LastKnownOnline = true,
                    LastRouterName = $"测试设备 {index}",
                    LastConnectionType = 2
                },
                Identity = new LanDeviceMonitor.Models.DeviceIdentity(
                    index == 1
                        ? "窗帘"
                        : index == 2
                            ? "空调"
                            : "手机",
                    index <= 2 ? string.Empty : "\uE8EA",
                    $"测试设备 {index}"),
                Sessions = index == 1
                    ?
                    [
                        new LanDeviceMonitor.Models.DeviceSession
                        {
                            MacAddress = "00:00:00:00:00:01",
                            OnlineAt = new DateTimeOffset(
                                2026, 7, 24, 18, 27, 7, TimeSpan.FromHours(8)),
                            OfflineAt = new DateTimeOffset(
                                2026, 7, 24, 20, 35, 7, TimeSpan.FromHours(8))
                        }
                    ]
                    : []
            }))
            .ToList();
        smokeWindow.DeviceList.ItemsSource = testDevices;
        smokeWindow.EmptyPanel.Visibility = System.Windows.Visibility.Collapsed;
        smokeWindow.SettingsPanel.Visibility = System.Windows.Visibility.Visible;
        smokeWindow.Show();
        smokeWindow.UpdateLayout();
        MicaWindowService.Apply(smokeWindow, useDarkMode: false);
        acrylicAccentApplied =
            MicaWindowService.LastAcrylicApplySucceeded &&
            MicaWindowService.LastSystemBackdropApplySucceeded;
        var deviceListScrollBar =
            FindVisualDescendant<System.Windows.Controls.Primitives.ScrollBar>(
                smokeWindow.DeviceList,
                scrollBar =>
                    scrollBar.Orientation ==
                        System.Windows.Controls.Orientation.Vertical &&
                    scrollBar.Visibility ==
                        System.Windows.Visibility.Visible);
        var settingsScrollBar =
            FindVisualDescendant<System.Windows.Controls.Primitives.ScrollBar>(
                smokeWindow.SettingsPanel,
                scrollBar =>
                    scrollBar.Orientation ==
                        System.Windows.Controls.Orientation.Vertical &&
                    scrollBar.Visibility ==
                        System.Windows.Visibility.Visible);
        deviceListScrollBarVisible = deviceListScrollBar is not null;
        settingsScrollBarVisible = settingsScrollBar is not null;
        deviceListScrollThumbHeight =
            FindVisualDescendant<System.Windows.Controls.Primitives.Thumb>(
                deviceListScrollBar)?.ActualHeight ?? 0d;
        settingsScrollThumbHeight =
            FindVisualDescendant<System.Windows.Controls.Primitives.Thumb>(
                settingsScrollBar)?.ActualHeight ?? 0d;
        var firstDeviceItem =
            smokeWindow.DeviceList.ItemContainerGenerator.ContainerFromIndex(0)
                as System.Windows.Controls.ListBoxItem;
        var deviceHoverTrigger = firstDeviceItem?.Template.Triggers
            .OfType<System.Windows.Trigger>()
            .FirstOrDefault(trigger =>
                trigger.Property == System.Windows.UIElement.IsMouseOverProperty &&
                Equals(trigger.Value, true));
        var deviceHoverEnterAnimation = deviceHoverTrigger?.EnterActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        var deviceHoverExitAnimation = deviceHoverTrigger?.ExitActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        deviceListHoverAnimationConfigured =
            deviceHoverEnterAnimation?.Duration.HasTimeSpan == true &&
            deviceHoverEnterAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(240) &&
            deviceHoverEnterAnimation.EasingFunction is
                System.Windows.Media.Animation.CubicEase &&
            deviceHoverExitAnimation?.Duration.HasTimeSpan == true &&
            deviceHoverExitAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(200) &&
            deviceHoverExitAnimation.EasingFunction is
                System.Windows.Media.Animation.QuadraticEase;
        if (!string.IsNullOrWhiteSpace(visualQaImagePath))
        {
            RenderWindowToPng(smokeWindow, visualQaImagePath);
        }
        if (!string.IsNullOrWhiteSpace(darkVisualQaImagePath))
        {
            ThemeService.Apply(ThemeService.DarkMode);
            typeof(MainWindow).GetMethod(
                    "UpdateSelectedDevice",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(smokeWindow, [testDevices[0], false]);
            smokeWindow.SettingsPanel.Visibility = System.Windows.Visibility.Collapsed;
            smokeWindow.EmptyPanel.Visibility = System.Windows.Visibility.Collapsed;
            smokeWindow.DetailPanel.Visibility = System.Windows.Visibility.Visible;
            smokeWindow.Width = 1100;
            smokeWindow.Height = 900;
            smokeWindow.UpdateLayout();
            detailIconThemeConfigured =
                smokeWindow.DetailDeviceIconBackground.Background is
                    System.Windows.Media.SolidColorBrush detailIconBrush &&
                detailIconBrush.Color ==
                    (System.Windows.Media.Color)application.Resources["DeviceIconColor"];
            smokeWindow.DetailPanel.ScrollToVerticalOffset(
                smokeWindow.DetailPanel.ScrollableHeight);
            smokeWindow.UpdateLayout();
            RenderWindowToPng(smokeWindow, darkVisualQaImagePath);
            ThemeService.Apply(ThemeService.LightMode);
        }
        smokeWindow.PrepareForExit();
        smokeWindow.Close();
        var menu = new TrayMenuWindow(
            () => trayMenuOpenInvoked = true,
            () => { })
        {
            Left = -10000,
            Top = -10000
        };
        menu.Show();
        menu.UpdateLayout();
        Check(
            menu.OpenIconViewbox.Width == 16d &&
            menu.ExitIconViewbox.Width == 16d,
            "托盘菜单两个矢量图标应使用一致的 16 DIP 画布");
        var openIconCenter = menu.OpenIconViewbox.TranslatePoint(
            new System.Windows.Point(
                menu.OpenIconViewbox.ActualWidth / 2d,
                menu.OpenIconViewbox.ActualHeight / 2d),
            menu);
        var openTextCenter = menu.OpenText.TranslatePoint(
            new System.Windows.Point(
                menu.OpenText.ActualWidth / 2d,
                menu.OpenText.ActualHeight / 2d),
            menu);
        trayOpenIconTextAligned =
            Math.Abs(openIconCenter.Y - openTextCenter.Y) <= 0.5d;
        var trayHoverTrigger = menu.OpenButton.Template.Triggers
            .OfType<System.Windows.Trigger>()
            .FirstOrDefault(trigger =>
                trigger.Property == System.Windows.UIElement.IsMouseOverProperty &&
                Equals(trigger.Value, true));
        var trayHoverAnimation = trayHoverTrigger?.EnterActions
            .OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(action => action.Storyboard.Children)
            .OfType<System.Windows.Media.Animation.DoubleAnimation>()
            .FirstOrDefault();
        trayMenuHoverAnimationConfigured =
            trayHoverAnimation?.Duration.HasTimeSpan == true &&
            trayHoverAnimation.Duration.TimeSpan ==
                TimeSpan.FromMilliseconds(180) &&
            trayHoverAnimation.EasingFunction is
                System.Windows.Media.Animation.CubicEase;
        if (!string.IsNullOrWhiteSpace(trayVisualQaImagePath))
        {
            RenderWindowToPng(menu, trayVisualQaImagePath);
        }
        var openMethod = typeof(TrayMenuWindow).GetMethod(
            "OpenButton_Click",
            BindingFlags.Instance | BindingFlags.NonPublic);
        openMethod!.Invoke(
            menu,
            [menu, new System.Windows.RoutedEventArgs()]);
        application.Shutdown();
    }
    catch (TargetInvocationException exception)
    {
        trayMenuOpenException = exception.InnerException ?? exception;
    }
    catch (Exception exception)
    {
        trayMenuOpenException = exception;
    }
});
trayMenuThread.SetApartmentState(ApartmentState.STA);
trayMenuThread.Start();
trayMenuThread.Join(TimeSpan.FromSeconds(5));
Check(
    !trayMenuThread.IsAlive &&
    trayMenuOpenException is null &&
    trayMenuOpenInvoked,
    $"主窗口启动与托盘菜单打开不应触发异常：{trayMenuOpenException}");
Check(
    verticalScrollBarVisible &&
    verticalScrollBarWidth >= 10d &&
    verticalScrollBarMaximum > 0d &&
    verticalScrollBarViewport > 0d &&
    verticalScrollThumbVisible,
    $"内容溢出时应显示 Windows 设置风格细滚动条：可见={verticalScrollBarVisible}，宽度={verticalScrollBarWidth:0.##}，最大值={verticalScrollBarMaximum:0.##}，视口={verticalScrollBarViewport:0.##}，滑块高度={verticalScrollThumbHeight:0.##}，最小高度={verticalScrollThumbMinimumHeight:0.##}");
Check(
    scrollThumbDragAnimationConfigured,
    "滚动条按住与松开应分别使用 300 ms 的二次缓入和缓出动画");
Check(
    scrollThumbHoverAnimationConfigured,
    "滚动条悬停与非悬停应分别使用 300 ms 的非线性动画");
Check(
    deviceListScrollBarVisible &&
    settingsScrollBarVisible &&
    deviceListScrollThumbHeight >= 28d &&
    settingsScrollThumbHeight >= 28d,
    $"主窗口左右板块内容溢出时都应显示具有实际高度的细滚动条：左侧可见={deviceListScrollBarVisible}，左侧滑块={deviceListScrollThumbHeight:0.##}，右侧可见={settingsScrollBarVisible}，右侧滑块={settingsScrollThumbHeight:0.##}");
Check(
    deviceListHoverAnimationConfigured,
    "左侧设备条块的悬停与非悬停状态应使用非线性动画");
Check(
    trayMenuHoverAnimationConfigured,
    "托盘菜单的两个选项应分别使用独立的非线性悬停动画");
Check(
    trayOpenIconTextAligned,
    "托盘菜单“打开主界面”的图标与文字应在同一垂直中心线上");
Check(
    themePaletteConfigured,
    "浅深色材质、下拉栏、时间轴与滚动条颜色资源应使用指定的不透明度和主题值");
Check(
    detailIconThemeConfigured,
    "深色模式右侧详情设备图标应与左侧设备列表共用适配主题的背景资源");
Check(
    acrylicAccentApplied,
    "Windows 应实际接受 Acrylic Accent 材质写入");
Check(
    WindowPlacementService.HasVisibleArea(
        new SavedWindowPlacement(120, 80, 1300, 840, false),
        [new System.Drawing.Rectangle(0, 0, 1920, 1040)]),
    "位于工作区内的窗口布局应当可以恢复");
Check(
    !WindowPlacementService.HasVisibleArea(
        new SavedWindowPlacement(-2200, 80, -1020, 840, false),
        [new System.Drawing.Rectangle(0, 0, 1920, 1040)]),
    "已离开所有屏幕的窗口布局不应恢复");

if (args.Contains("--router", StringComparer.OrdinalIgnoreCase))
{
    var credentialService = new RouterCredentialService();
    var password = credentialService.ReadPassword("10.77.0.1");
    Check(!string.IsNullOrWhiteSpace(password), "Windows 凭据管理器中没有路由器密码");
    if (!string.IsNullOrWhiteSpace(password))
    {
        using var client = new XiaomiRouterClient();
        client.Configure("10.77.0.1", password);
        var snapshots = await client.GetDeviceSnapshotsAsync();
        Check(snapshots.Count > 0, "路由器没有返回设备快照");
        Check(
            snapshots.All(snapshot =>
                snapshot.UploadSpeedBytes >= 0 &&
                snapshot.DownloadSpeedBytes >= 0 &&
                snapshot.TotalUploadBytes >= 0 &&
                snapshot.TotalDownloadBytes >= 0),
            "设备流量字段包含无效值");
        var internetInfo = await client.GetInternetInfoAsync();
        Check(
            !string.IsNullOrWhiteSpace(internetInfo.Ipv4ConnectionType) &&
            !string.IsNullOrWhiteSpace(internetInfo.Ipv4Address),
            "路由器没有返回有效的 IPv4 上网信息");
        Check(
            !string.IsNullOrWhiteSpace(internetInfo.Ipv6ConnectionType),
            "路由器没有返回 IPv6 连接模式");
        var meshNodes = await client.GetMeshNodesAsync();
        Check(meshNodes.Count > 0, "路由器没有返回 Mesh 节点");
        Check(
            meshNodes.Any(node => node.IsGateway) &&
            meshNodes.All(node => node.ConnectedDeviceCount >= 0),
            "Mesh 节点或设备数量无效");
        Console.WriteLine(
            $"路由器联调：已读取 {snapshots.Count} 台在线设备和 {meshNodes.Count} 个 Mesh 节点。");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"自检失败：{failures.Count} 项");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine($"自检通过：设备识别、单位换算、通知时段、受控滚动、细滚动条、版本显示、主窗口启动、托盘菜单与窗口布局共 {checksRun} 项；路由器模式另验证上网与 Mesh 信息。");
return 0;

void Check(bool condition, string message)
{
    checksRun++;
    if (!condition)
    {
        failures.Add(message);
    }
}

T? FindVisualDescendant<T>(
    System.Windows.DependencyObject? root,
    Func<T, bool>? predicate = null)
    where T : System.Windows.DependencyObject
{
    if (root is null)
    {
        return null;
    }

    for (var index = 0;
         index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
         index++)
    {
        var child =
            System.Windows.Media.VisualTreeHelper.GetChild(root, index);
        if (child is T match && (predicate is null || predicate(match)))
        {
            return match;
        }

        var descendant = FindVisualDescendant(child, predicate);
        if (descendant is not null)
        {
            return descendant;
        }
    }

    return null;
}

void RenderWindowToPng(
    System.Windows.Window window,
    string outputPath)
{
    window.UpdateLayout();
    var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
    var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
    var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
        width,
        height,
        96,
        96,
        System.Windows.Media.PixelFormats.Pbgra32);
    target.Render(window);
    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
    encoder.Frames.Add(
        System.Windows.Media.Imaging.BitmapFrame.Create(target));
    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    using var stream = File.Create(outputPath);
    encoder.Save(stream);
}

int RunStartupSmoke()
{
    Exception? startupException = null;
    var windowShown = false;
    var thread = new Thread(() =>
    {
        LanDeviceMonitor.App? application = null;
        try
        {
            application = new LanDeviceMonitor.App();
            application.DispatcherUnhandledException += (_, eventArgs) =>
            {
                startupException = eventArgs.Exception;
                eventArgs.Handled = true;
                application.Shutdown(2);
            };
            application.InitializeComponent();
            var activationTimer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromSeconds(5),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                async (sender, _) =>
                {
                    ((System.Windows.Threading.DispatcherTimer)sender!).Stop();
                    await SingleInstanceService.SignalPrimaryAsync();
                },
                application.Dispatcher);
            var timer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromSeconds(45),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                (_, _) =>
                {
                    windowShown = application.Windows
                        .OfType<MainWindow>()
                        .Any(window => window.IsVisible);
                    application.Shutdown();
                },
                application.Dispatcher);
            activationTimer.Start();
            timer.Start();
            application.Run();
        }
        catch (Exception exception)
        {
            startupException = exception;
            application?.Shutdown(2);
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(TimeSpan.FromSeconds(55));

    if (thread.IsAlive)
    {
        Console.Error.WriteLine("启动冒烟测试超时，应用未能在预期时间内退出。");
        return 1;
    }

    if (startupException is not null)
    {
        Console.Error.WriteLine($"启动冒烟测试异常：{startupException}");
        return 1;
    }

    if (!windowShown)
    {
        Console.Error.WriteLine("启动冒烟测试未检测到可见主窗口。");
        return 1;
    }

    Console.WriteLine("启动冒烟测试通过：主窗口已显示，45 秒内无未处理异常。");
    return 0;
}
