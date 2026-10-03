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
    private static void Require(bool condition,string message)
    {
        if(!condition) throw new InvalidOperationException(message);
    }
    public static int Run(bool popupOnly = false, bool performanceOnly = false, bool responsiveOnly = false, bool refinementOnly = false, bool restoredWindowOnly = false, bool sidebarPolishOnly = false)
    {
        Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory,".tools","ui-dev238"));
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
                // Startup may receive cached history before the first visual measure.
                var populatedHistory = new ItemsControl
                {
                    ItemsSource = new[] { "first", "second", "third" },
                    ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(SessionHistoryPanel)))
                };
                var startupHost = new Window { Content=populatedHistory, Width=400, Height=300,
                    Left=-10000, Top=-10000, ShowActivated=false, ShowInTaskbar=false };
                try { startupHost.Show(); startupHost.UpdateLayout(); }
                finally { startupHost.Close(); }
                Require(populatedHistory.ItemContainerGenerator.ContainerFromIndex(2) is not null,
                    "首次布局前已经存在的历史数据未呈现");
                Console.WriteLine("Prepopulated history startup PASS.");
                window = new MainWindow(new NotificationService())
                { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
                // Never initialize monitoring or change the user's persisted settings in a UI test.
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                var fixtureTime = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(8));
                var devices = Enumerable.Range(0, 12).Select(index => index < 10).Select((online, index) => new LanDeviceMonitor.Models.DeviceViewData
                {
                    Profile = new LanDeviceMonitor.Models.DeviceProfile
                    {
                        MacAddress = $"00:00:00:00:30:{index + 1:X2}", LastRouterName = index == 0 ? "测试笔记本" : $"测试设备 {index + 1}",
                        LastKnownOnline = online, LastSeenAt = fixtureTime, FirstSeenAt = fixtureTime,
                        LastIpAddress = $"192.168.31.{100 + index}",
                        LastDownloadSpeedBytes = index == 9 ? 999 * 1024 * 1024 : index * 1024
                    },
                    Identity = new LanDeviceMonitor.Models.DeviceIdentity("笔记本电脑", "\uE770", index == 0 ? "测试笔记本" : $"测试设备 {index + 1}"),
                    Sessions = (performanceOnly ? Enumerable.Range(1,100).Select(i=>i*62) : new[] { 62, 1550 }).Select(minutes => new LanDeviceMonitor.Models.DeviceSession
                    {
                        MacAddress = $"00:00:00:00:30:{index + 1:X2}",
                        OnlineAt = fixtureTime.AddMinutes(-minutes - 10),
                        OfflineAt = fixtureTime.AddMinutes(-10)
                    }).ToList()
                }).ToArray();
                typeof(MainWindow).GetMethod("ReplaceDevices", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [devices, false, false]);
var shellIcon = new BitmapImage(); shellIcon.BeginInit(); shellIcon.CacheOption=BitmapCacheOption.OnLoad;
                shellIcon.UriSource=new Uri(Path.Combine(Environment.CurrentDirectory,"src","LanDeviceMonitor.App","Assets","AppIcon.png")); shellIcon.EndInit(); shellIcon.Freeze();
                window.ShellAppIcon.Source=shellIcon;
                if (popupOnly) window.Opacity = 0;
                window.Show(); window.UpdateLayout();
                Pump(600);
                if (sidebarPolishOnly) { VerifySidebarPolish(window); return; }
                if (restoredWindowOnly) { VerifyRestoredWindow(window); return; }
                if (performanceOnly) { ProfileDeviceTransitions(window); return; }
                if (responsiveOnly) { VerifyResponsiveNavigation(window); return; }
                if (refinementOnly) { VerifyRefinements(window); return; }
                var profileGuard=typeof(MainWindow).GetField("_isPopulatingDeviceSettings",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var oldGuard=profileGuard.GetValue(window);profileGuard.SetValue(window,true);
                var placeholder=(TextBlock)window.CustomNameBox.Template.FindName("Placeholder",window.CustomNameBox);
                Require(placeholder.Visibility==Visibility.Visible && window.CustomNameBox.Text=="", "空名称应显示提示，提示不可写入真实文本");
                window.CustomNameBox.Text="测试名称"; Pump(20);
                Require(placeholder.Visibility==Visibility.Collapsed,"有文字时占位提示未隐藏");
                window.CustomNameBox.Clear();profileGuard.SetValue(window,oldGuard);
                window.SidebarToggleButton.Focus();Pump(20);
                Require(((Border)window.SidebarToggleButton.Template.FindName("IconHover",window.SidebarToggleButton)).Opacity==0,"键盘焦点被误当作鼠标悬停亮层");
                Require(window.DetailDeviceIconBackground.CornerRadius.TopLeft==window.DetailDeviceIconBackground.ActualHeight/2,"详情设备图标底色不是圆形");
                if (popupOnly) { VerifyPopupAlignment(window); return; }
                KeepTestPopupsOffDesktop(window);
                foreach (var option in DeviceIdentityService.AvailableTypes)
                {
                    var icon = new DeviceTypeIcon { DeviceType=option.TypeName };
                    var bounds=icon.IconGeometry.GetRenderBounds(new Pen(Brushes.Black,1.7));
                    Require(bounds.Left>=0 && bounds.Top>=0 && bounds.Right<=24 && bounds.Bottom<=24,
                        $"设备图标超出24px画布: {option.TypeName} {bounds}");
                }
                Require(DeviceIdentityService.AvailableTypes.Any(t=>t.TypeName=="摄像头") &&
                    DeviceIdentityService.Identify("我的摄像头","",1).TypeName=="摄像头", "摄像头类型或识别缺失");
                ExportDeviceIconAtlas(window);
                window.DetailPanel.ScrollToBottom(); Pump(120);
                Require(window.DetailPanel.VerticalOffset > 0, "滚动重置回归缺少可滚动内容");
                window.DeviceList.SelectedIndex = 1; Pump(1150);
                Require(window.DetailPanel.VerticalOffset < .1, "切换设备后详情未回到顶部");
                window.DeviceFilterSegments.SelectedIndex = 1; Pump(60);
                Require(window.SelectedDevice?.IsOnline == true, "详情应先完成退出段再替换内容");
                if (SystemParameters.ClientAreaAnimation)
                {
                    var listProgress = 1 - Math.Abs(((TranslateTransform)window.GetDeviceListMotion(window.DeviceListTransition).RenderTransform).X) / 340;
                    var filterIndicator = (Border)window.DeviceFilterSegments.Template.FindName("PART_Indicator",window.DeviceFilterSegments);
                    var filterProgress = ((TranslateTransform)filterIndicator.RenderTransform).X / filterIndicator.ActualWidth;
                    Console.WriteLine($"SYNC at 60ms: list={listProgress:F3}, selector={filterProgress:F3}, detail={window.DetailPanel.Opacity:F3}");
                    Require(Math.Abs(listProgress-filterProgress)<.12 && window.DetailPanel.Opacity > 0 && window.DetailPanel.Opacity < 1, "列表与筛选不同步，或详情未同时开始退出段");
                }
                Pump(1100); window.DeviceFilterSegments.SelectedIndex = 0; Pump(1100);
                foreach (var button in new[] { window.SidebarRailRouterButton, window.SidebarRailSettingsButton })
                {
                    if (button.Content is not TextBlock glyphText) continue;
                    var formatted = new FormattedText(glyphText.Text, System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, new Typeface(glyphText.FontFamily, glyphText.FontStyle, glyphText.FontWeight, glyphText.FontStretch),
                        glyphText.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(glyphText).PixelsPerDip);
                    var ink = glyphText.TransformToAncestor(button).TransformBounds(formatted.BuildGeometry(new Point()).Bounds);
                    Console.WriteLine($"ICON CENTER {button.Name}: dx={ink.Left + ink.Width / 2 - button.ActualWidth / 2:F3}, dy={ink.Top + ink.Height / 2 - button.ActualHeight / 2:F3}");
                    Require(Math.Abs(ink.Left + ink.Width / 2 - button.ActualWidth / 2) < .1 &&
                        Math.Abs(ink.Top + ink.Height / 2 - button.ActualHeight / 2) < .1, $"按钮图标未按字形居中: {button.Name}");
                }
                Require(window.CustomNameBox.FontFamily.Source == window.ShellAppName.FontFamily.Source &&
                    window.CustomNameBox.FontWeight == FontWeights.Normal && window.DeviceTypeCombo.FontWeight == FontWeights.Normal,
                    $"表单文字应与普通界面文字采用同一字体及正常字重: input={window.CustomNameBox.FontFamily}, title={window.ShellAppName.FontFamily}, weights={window.CustomNameBox.FontWeight}/{window.DeviceTypeCombo.FontWeight}");
                foreach (var familyKey in new[] { "AppTextFont", "AppIconFont" })
                {
                    var family = (FontFamily)window.FindResource(familyKey);
                    var face = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                    var expectedScheme = Environment.GetEnvironmentVariable("LAN_FONT_PROOF") == "baseline" ? "file" : "pack";
                    Require(face.TryGetGlyphTypeface(out var actualGlyph) && actualGlyph.FontUri.Scheme == expectedScheme,
                        $"字体没有从内嵌资源加载: {familyKey}, {family.Source}");
                    Console.WriteLine($"Embedded font: {familyKey} = {actualGlyph!.FontUri}");
                }
                window.DeviceTypeCombo.IsDropDownOpen = true; Pump(650);
                var iconPopup = (System.Windows.Controls.Primitives.Popup)window.DeviceTypeCombo.Template.FindName("PART_Popup", window.DeviceTypeCombo);
                var typeTrigger = (System.Windows.Controls.Primitives.ToggleButton)window.DeviceTypeCombo.Template.FindName("DropDownToggle",window.DeviceTypeCombo);
                var typeTriggerRoot = (FrameworkElement)typeTrigger.Template.FindName("StudioRoot",typeTrigger);
                Require(!typeTriggerRoot.RenderTransform.HasAnimatedProperties, "静止下拉框仍有缩放动画时钟");
                var deviceIcons = Descendants<DeviceTypeIcon>(iconPopup.Child).ToArray();
                Require(deviceIcons.Length > 0, "设备类型菜单缺少图标");
                foreach (var icon in deviceIcons)
                {
                    Require(!icon.IconGeometry.Bounds.IsEmpty && icon.IconGeometry.Bounds.Width > 0, $"设备矢量图标缺失: {icon.DeviceType}");
                }
                CaptureElement((FrameworkElement)iconPopup.Child,"device-types-Light.png");
                window.DeviceTypeCombo.IsDropDownOpen = false; Pump(250);
                var originalTypeIndex = window.DeviceTypeCombo.SelectedIndex;
                typeof(MainWindow).GetField("_isPopulatingDeviceSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    ThemeService.Apply(mode);
                    for (var index = 0; index < window.DeviceTypeCombo.Items.Count; index++)
                    {
                        window.DeviceTypeCombo.SelectedIndex = index; Pump(30);
                        foreach (var icon in Descendants<DeviceTypeIcon>(window.DeviceTypeCombo))
                        {
                            Require(!icon.IconGeometry.Bounds.IsEmpty && icon.IconGeometry.Bounds.Width > 0, $"设备矢量图标缺失: {icon.DeviceType}");
                        }
                    }
                    window.DeviceTypeCombo.IsDropDownOpen = true; Pump(650);
                    CaptureElement((FrameworkElement)iconPopup.Child,$"device-types-{mode}.png");
                    window.DeviceTypeCombo.IsDropDownOpen = false; Pump(250);
                }
                window.DeviceTypeCombo.SelectedIndex = originalTypeIndex;
                typeof(MainWindow).GetField("_isPopulatingDeviceSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
                ThemeService.Apply(ThemeService.LightMode);
                foreach (var pair in new[] { (0, 1), (2, 10), (1, 5), (0, 1), (2, 10) })
                {
                    window.TrafficRangeSegments.SelectedIndex = pair.Item1;
                    Require(window.TrafficRangeMinutes == pair.Item2, "统计时段未同步");
                }
                var segments = window.DeviceFilterSegments;
                segments.SelectedIndex = 1; segments.SelectedIndex = 0; segments.SelectedIndex = 1;
                Pump(300);
                var indicator = (Border)segments.Template.FindName("PART_Indicator", segments);
                VerifySegmentText(segments);
                VerifySegmentText(window.TrafficRangeSegments);
                var hoverItem = (ListBoxItem)segments.Items[0];
                var idleColor = ((SolidColorBrush)hoverItem.Foreground).Color;
                var activeColor = ((SolidColorBrush)((SegmentItem)hoverItem).ActiveBrush).Color;
                void Hover(bool enter) => hoverItem.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                { RoutedEvent = enter ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent });
                Hover(true);
                Require(((SolidColorBrush)hoverItem.Foreground).HasAnimatedProperties || !SystemParameters.ClientAreaAnimation,
                    "悬停事件必须启动动画");
                Pump(55);
                var halfwayColor = ((SolidColorBrush)hoverItem.Foreground).Color;
                Require(halfwayColor != idleColor, "未选中项缺少悬停反馈");
                if (SystemParameters.ClientAreaAnimation)
                    Require(halfwayColor != activeColor, "悬停进入必须存在中间帧而非瞬间切换");
                Hover(false);
                Require(((SolidColorBrush)hoverItem.Foreground).Color == halfwayColor || !SystemParameters.ClientAreaAnimation,
                    "悬停反向必须从当前颜色继续");
                Pump(240);
                Require(((SolidColorBrush)hoverItem.Foreground).Color == idleColor, "悬停结束没有恢复灰色");
                Hover(true); Pump(240);
                Require(((SolidColorBrush)hoverItem.Foreground).Color == activeColor, "悬停进入没有到达主文字色");
                Hover(false); Pump(240);
                segments.Focus(); Pump(50);
                Require(segments.Template.FindName("FocusRing", segments) is null, "分段仍含蓝色焦点框");
                var scale = VisualTreeHelper.GetDpi(segments).DpiScaleY;
                var expectedPadding = 2 * Math.Round(1.5 * scale) / scale;
                Require(Math.Abs(segments.ActualHeight - indicator.ActualHeight - expectedPadding) < 0.1, $"分段上下留白应各为 1.5: control={segments.ActualHeight}, indicator={indicator.ActualHeight}");
                Require(Math.Abs(((TranslateTransform)indicator.RenderTransform).X - indicator.ActualWidth) < 1, "滑块没有到达离线分段");
                segments.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Home) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Require(segments.SelectedIndex == 0, "Home 键未返回首段");
                Pump(1200);
                VerifySegmentText(segments);
                window.Background = new SolidColorBrush(Color.FromRgb(242,243,247));
                Require(window.ShellAppName.Text == "局域网设备助手" && window.ShellAppName.IsVisible && window.ShellAppIcon.IsVisible,
                    "标题区缺少软件图标或名称");
                Require(window.DeviceSidebar.CornerRadius == window.DeviceNetworkInfoCard.CornerRadius &&
                    window.DetailSurface.CornerRadius == window.DeviceNetworkInfoCard.CornerRadius &&
                    window.DeviceSidebar.BorderThickness == new Thickness(1) && window.DetailSurface.BorderThickness == new Thickness(1), "左右内容应使用独立的统一圆角卡片");
                Require(System.Windows.Automation.AutomationProperties.GetName(window.SidebarRailSettingsButton) == "设置" && System.Windows.Automation.AutomationProperties.GetName(window.SidebarRailRouterButton) == "路由器", "底部图标缺少可访问名称");
                WindowFrameHitTests.Verify(window);
                Require(window.DetailPanel.FocusVisualStyle is null, "详情滚动区域仍有默认虚线焦点框");
                Require(ReferenceEquals(window.DetailStatusText.Foreground, window.FindResource("TextBrush")), "连接状态应使用普通文字颜色");
                foreach (var dot in Descendants<System.Windows.Shapes.Ellipse>(window.DetailPanel).Where(x=> x.Fill == window.FindResource("TimelineOfflineDotBrush") || x.Fill == window.FindResource("TimelineOnlineDotBrush")))
                    Require(dot.Width == 9 && dot.Height == 9, "历史记录的小点尺寸被误改");
var iconLeft = window.ShellAppIcon.TranslatePoint(new Point(),window.SidebarExpandedContent).X;
                var tangentX = window.DeviceFilterSegments.TranslatePoint(new Point(),window.SidebarExpandedContent).X + window.DeviceFilterSegments.ActualHeight * 0.3;
                Require(Math.Abs(iconLeft-tangentX)<0.1,$"软件图标左缘未对齐筛选胶囊水平切点 icon={iconLeft} tangent={tangentX} margin={window.ShellIdentityRow.Margin} height={window.DeviceFilterSegments.ActualHeight}");
                Require(window.ShellAppName.FontSize == 18 && window.ShellAppName.FontWeight == FontWeights.Medium && Math.Abs(window.ShellAppIcon.ActualHeight - 30) <= 1, $"标题字号或图标尺寸错误: {window.ShellAppName.FontSize}, {window.ShellAppName.FontWeight}, {window.ShellAppIcon.ActualHeight}");
                Require(window.FindName("CopyDeviceIpButton") is null && window.FindName("CopyDeviceMacButton") is null, "连接信息不应保留复制按钮");
                window.SidebarColumn.Width = new GridLength(310); window.UpdateLayout();
                var selectedBeforeCollapse = window.SelectedDevice;
                var collapseStartWidth=window.SidebarColumn.ActualWidth;
                var collapseClock=System.Diagnostics.Stopwatch.StartNew();
                window.SidebarToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if(SystemParameters.ClientAreaAnimation)
                    PumpUntil(()=>window.SidebarColumn.ActualWidth> MainWindow.CollapsedSidebarWidth && window.SidebarColumn.ActualWidth<collapseStartWidth-.5);
                var intermediateWidth = window.SidebarColumn.ActualWidth;
                if (SystemParameters.ClientAreaAnimation)
                    Require(intermediateWidth > MainWindow.CollapsedSidebarWidth && intermediateWidth < collapseStartWidth, $"侧栏收起必须存在动画中间帧 width={intermediateWidth}, elapsed={collapseClock.ElapsedMilliseconds}");
                Capture(window, "sidebar-mid-transition.png", ThemeService.LightMode);
                window.CollapsedSidebarButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                window.UpdateLayout();
                Require(Math.Abs(window.SidebarColumn.ActualWidth - intermediateWidth) < 2 || !SystemParameters.ClientAreaAnimation,
                    "侧栏反向动画发生跳变");
                Pump(350);
                Require(Math.Abs(window.SidebarColumn.ActualWidth - collapseStartWidth) < 1, "反向展开没有恢复侧栏宽度");
                window.SidebarToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump(350);
                Require(window.SidebarColumn.ActualWidth == MainWindow.CollapsedSidebarWidth && window.SidebarExpandedContent.Visibility == Visibility.Collapsed &&
                    window.CollapsedSidebarButton.IsVisible && !window.SidebarRailSettingsButton.IsVisible, "收起后应仅保留统一窄栏");
                Require(window.SidebarDividerLine.IsVisible && window.SidebarDividerColumn.ActualWidth + 0.001 >= 1 / VisualTreeHelper.GetDpi(window).DpiScaleX, $"收起侧栏缺少分界线 width={window.SidebarDividerColumn.ActualWidth} line={window.SidebarDividerLine.ActualWidth} visible={window.SidebarDividerLine.IsVisible}");
                Capture(window, "sidebar-collapsed.png", ThemeService.LightMode);
                window.TrafficRangeSegments.Focus();
                Pump(220);
                window.CollapsedSidebarButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                Require(window.CollapsedSidebarGlyph.Opacity == 1 && window.FindName("CollapsedAppIcon") is null, "收起后应直接显示侧栏按钮，不切换软件图标");
                Capture(window, "sidebar-collapsed-hover.png", ThemeService.LightMode);
                window.CollapsedSidebarButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseLeaveEvent});
                Pump(200); Require(window.CollapsedSidebarGlyph.Opacity==1,"移出后不应隐藏展开图标");
                var blankHit = window.CollapsedSidebarButton.InputHitTest(new Point(28, window.CollapsedSidebarButton.ActualHeight * 0.7));
                Require(window.CollapsedRouterButton.IsVisible && window.CollapsedSettingsButton.IsVisible, "收起后必须保留两个独立圆形导航按钮");
                var expandPeer = UIElementAutomationPeer.CreatePeerForElement(window.CollapsedSidebarButton)!;
                ((IInvokeProvider)expandPeer.GetPattern(PatternInterface.Invoke)!).Invoke(); Pump(350);
                Require(Math.Abs(window.SidebarColumn.ActualWidth - collapseStartWidth) < 1 && ReferenceEquals(selectedBeforeCollapse, window.SelectedDevice), "点击窄栏应展开并保留选中设备");
                window.SidebarRailSettingsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true); Pump(550);
                Require(window.SettingsPanel.Visibility == Visibility.Visible, "合并后的设置入口失效");
                var selectedRow=(ListBoxItem)window.DeviceList.ItemContainerGenerator.ContainerFromItem(window.DeviceList.SelectedItem);
                var selectedRowText=Descendants<TextBlock>(selectedRow).First();
                selectedRowText.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=Mouse.PreviewMouseDownEvent }); Pump(550);
                window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=Mouse.PreviewMouseUpEvent }); Pump(280);
                Require(window.DetailPanel.Visibility == Visibility.Visible, "合并导航无法返回设备详情");
                window.SidebarColumn.Width = new GridLength(390); window.UpdateLayout();
                Capture(window, "devices-Light.png", ThemeService.LightMode);
                Require(window.DetailConnectionText.Text == window.SelectedDevice!.ConnectionText.Replace("网线接入", "网线") &&
                    window.DetailIpAddressText.Text == window.SelectedDevice.IpAddress &&
                    window.DetailMacAddressText.Text == window.SelectedDevice.MacAddress,
                    "独立网络信息栏必须对应当前设备");
                var deviceItem = (ListBoxItem)window.DeviceList.ItemContainerGenerator.ContainerFromIndex(0);
                var rowSurface = Descendants<Border>(deviceItem).First(border => border.Name == "ItemBorder");
                Require(Math.Abs(rowSurface.ActualWidth - window.DeviceFilterSegments.ActualWidth) < 1, "设备胶囊与筛选滑块不等宽");
                var listScroll = Descendants<ScrollViewer>(window.DeviceList).First();
                var bar = (System.Windows.Controls.Primitives.ScrollBar)listScroll.Template.FindName("PART_VerticalScrollBar", listScroll);
                Require(bar.IsVisible && bar.ActualWidth > 0 && bar.TranslatePoint(new Point(), window.DeviceList).X >= rowSurface.ActualWidth - 1, "滚动条没有移至胶囊右侧");
                Console.WriteLine($"SCROLL bar={bar.ActualWidth}x{bar.ActualHeight} max={bar.Maximum} viewport={bar.ViewportSize} position={bar.TranslatePoint(new Point(), window)} thumb={Descendants<System.Windows.Controls.Primitives.Thumb>(bar).First().ActualHeight}");
                listScroll.ScrollToEnd(); Pump(70);
                Require(listScroll.VerticalOffset > 0, "设备列表无法滚动");
                listScroll.ScrollToHome(); Pump(70);
                Capture(window, "device-scrollbar-verified.png", ThemeService.LightMode);
                window.ConnectionStatusText.Text = "已连接";
                window.LastRefreshText.Text = "更新于 12:34:56"; Pump(30);
                Require(window.ConnectionStatusText.Visibility == Visibility.Collapsed && window.LastRefreshText.IsVisible && window.LastRefreshText.FontSize == 14 && window.ConnectionDot.Width == 12, "已连接应仅显示较大的更新时间与圆点");
                window.ConnectionStatusText.Text = "连接异常"; Pump(30);
                Require(window.ConnectionStatusText.IsVisible && window.LastRefreshText.Visibility == Visibility.Collapsed, "异常状态不可被更新时间覆盖");
                var thumb = Descendants<System.Windows.Controls.Primitives.Thumb>(bar).First();
                var thumbVisual = Descendants<Border>(thumb).First(x => x.Name == "ThumbDefaultVisual");
                var thumbX = thumbVisual.TranslatePoint(new Point(thumbVisual.ActualWidth / 2, 0), window.DeviceSidebar).X;
                var capsuleRight = rowSurface.TranslatePoint(new Point(rowSurface.ActualWidth, 0), window.DeviceSidebar).X;
                Require(thumbX > capsuleRight && thumbX < window.DeviceSidebar.ActualWidth, "滚动条必须位于胶囊右缘与侧栏边缘之间");
                var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
                var pressedKey = (DependencyPropertyKey)typeof(System.Windows.Controls.Primitives.ButtonBase).GetField("IsPressedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                {
                    ThemeService.Apply(mode);
                    window.Background = new SolidColorBrush(mode == ThemeService.DarkMode ? Color.FromRgb(30,32,38) : Color.FromRgb(242,243,247));
                    foreach (var footer in new[] { window.SidebarRailRouterButton, window.SidebarRailSettingsButton })
                    {
                        var hover = (Border)footer.Template.FindName("CircleHover", footer);
                        var pressed = (Border)footer.Template.FindName("CirclePressed", footer);
                        footer.SetValue(hoverKey, true); Pump(150);
                        Require(hover.Opacity == 1, "底部按钮缺少悬停态");
                        Require(hover.Margin.Left>=footer.BorderThickness.Left,"悬停亮层覆盖了按钮描边");
                        var actualInk=footer.Content is DeviceTypeIcon vector ? vector.IconBrush : ((TextBlock)footer.Content).Foreground;
                        Require(((SolidColorBrush)actualInk).Color==(Color)window.FindResource("FooterIconHoverColor"),"底部按钮图标没有随悬停提亮");
                        CaptureElement(footer,$"footer-{footer.Name}-{mode}-hover.png");
                        footer.SetValue(pressedKey, true); Pump(30);
                        Require(pressed.Opacity == 1, "底部按钮缺少点按态");
                        CaptureElement(footer,$"footer-{footer.Name}-{mode}-pressed.png");
                        footer.SetValue(pressedKey, false); footer.SetValue(hoverKey, false); Pump(150);
                        Require(hover.Opacity == 0 && pressed.Opacity == 0, "底部按钮状态残留");
                    }
                    Capture(window, $"devices-{mode}.png", mode);
                }
                ThemeService.Apply(ThemeService.LightMode);
                Capture(window, "footer-and-controls.png", ThemeService.LightMode);

                var rowIcon = Descendants<Border>(deviceItem).First(border => border.Name == "DeviceRowIconBackground");
                var iconPosition = rowIcon.TranslatePoint(new Point(0,0), rowSurface);
                var bottomSpace = rowSurface.ActualHeight - iconPosition.Y - rowIcon.ActualHeight;
                const double pixelTolerance = 0.001;
                Require(Math.Abs(iconPosition.X - iconPosition.Y) <= pixelTolerance && Math.Abs(iconPosition.Y - bottomSpace) <= pixelTolerance,
                    $"图标四周间距不均: left={iconPosition.X}, top={iconPosition.Y}, bottom={bottomSpace}");
                var rateText = Descendants<TextBlock>(deviceItem).First(text => text.Name == "DeviceListRate");
                Require(rateText.Inlines.OfType<System.Windows.Documents.Run>().Count() == 3 &&
                    rateText.Inlines.All(inline => inline.BaselineAlignment == BaselineAlignment.Baseline),
                    "速率数值和单位必须在同一文本行共享基线");
                Require(deviceItem.ActualHeight >= 52 && deviceItem.ActualHeight <= 54, "单行设备胶囊没有压缩高度");
                Require(!Descendants<TextBlock>(deviceItem).Any(text => text.Text is "在线" or "离线" or "网线接入"),
                    "左侧设备条目仍含状态或连接信息");
                window.Width = 980; window.Height = 650; Pump(250);
                Require(window.SidebarColumn.ActualWidth <= window.WorkspaceGrid.ActualWidth - 605, "窄窗口没有限制侧栏宽度");
                Require(window.DetailNetworkMetadataRow.Children.OfType<StackPanel>().Count() == 4, "连接信息未横向排列");
                Capture(window, "devices-Light-980.png", ThemeService.LightMode);
                Require(Math.Abs(indicator.CornerRadius.TopLeft - indicator.ActualHeight / 2) < 0.1, "胶囊圆角不是高度的一半");
                typeof(MainWindow).GetMethod("ShowSettingsPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Pump(500);
                window.RouterAddressBox.Text = "10.77.0.1";
                typeof(MainWindow).GetField("_hasSavedRouterPassword",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);
                window.RouterPasswordBox.Password="";
                typeof(MainWindow).GetMethod("RefreshPasswordIndicator",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                Require(window.SavedRouterPasswordMask.Visibility==Visibility.Visible,"已有凭据没有掩码提示");
                window.RouterPasswordBox.Password="synthetic";
                Require(window.SavedRouterPasswordMask.Visibility==Visibility.Collapsed,"输入新密码时仍叠加已保存占位");
                window.RouterPasswordBox.Password="";
                window.DoNotDisturbStartBox.Text = "22:30";
                window.DoNotDisturbEndBox.Text = "08:00";
                window.RefreshIntervalCombo.SelectedIndex = 0;
                window.ThemeModeCombo.SelectedIndex = 0;

                var labelText = Descendants<TextBlock>(window.ThemeModeCombo).First(t => t.Text == "跟随系统");
                Console.WriteLine($"TEXT START input={window.RouterAddressBox.GetRectFromCharacterIndex(0).Left:F3}, dropdown={labelText.TranslatePoint(new Point(), window.ThemeModeCombo).X:F3}");
                Require(Math.Abs(window.RouterAddressBox.GetRectFromCharacterIndex(0).Left - labelText.TranslatePoint(new Point(), window.ThemeModeCombo).X) < .1,
                    "下拉文字起点与输入框不一致");
                window.ThemeModeCombo.IsDropDownOpen = true; Pump(90);
                var selectPopup = (System.Windows.Controls.Primitives.Popup)window.ThemeModeCombo.Template.FindName("PART_Popup", window.ThemeModeCombo);
                Require(selectPopup.IsOpen && selectPopup.Child.Opacity > 0, "下拉菜单未打开");
                Pump(600);
                var trigger = (System.Windows.Controls.Primitives.ToggleButton)window.ThemeModeCombo.Template.FindName("DropDownToggle",window.ThemeModeCombo);
                trigger.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                trigger.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent }); Pump(150);
                var selectFace=(Border)trigger.Template.FindName("StudioSurface",trigger);
                var selectRoot=(FrameworkElement)trigger.Template.FindName("StudioRoot",trigger);
                Require(((SolidColorBrush)selectFace.Background).Color == (Color)window.FindResource("StudioSurfaceColor"), "点按下拉框不应改变胶囊底色");
                Require(((ScaleTransform)selectRoot.RenderTransform).ScaleY < .99, "下拉框点按缩放丢失");
                trigger.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent }); Pump(650);
                Require(Math.Abs(((ScaleTransform)selectRoot.RenderTransform).ScaleY-1)<.001, "下拉框点按未恢复");
                Require(!selectPopup.Child.HasAnimatedProperties && !selectPopup.Child.RenderTransform.HasAnimatedProperties, "下拉弹层展开结束后仍残留动画时钟");
                Require(!selectRoot.RenderTransform.HasAnimatedProperties, "下拉按压结束后仍残留缩放动画时钟");
                Require(((Border)selectPopup.Child).CornerRadius == window.DeviceNetworkInfoCard.CornerRadius, "弹层圆角未统一");
                Require(window.RouterAddressBox.FontWeight == FontWeights.Normal && window.CustomNameBox.FontWeight == FontWeights.Normal, "输入文字应与普通文字使用相同字重");
                var arrow = (FrameworkElement)trigger.Template.FindName("StudioArrow",trigger);
                Require(Math.Abs(((RotateTransform)arrow.RenderTransform).Angle-180)<.01, "下拉箭头未旋转180度");
                Require(((Border)selectPopup.Child).ActualWidth >= window.ThemeModeCombo.ActualWidth, $"弹层不能窄于触发胶囊: {((Border)selectPopup.Child).ActualWidth}");
                var choice = (ComboBoxItem)window.ThemeModeCombo.Items[0];
                Require(choice.FontSize==window.ThemeModeCombo.FontSize && Math.Abs(choice.ActualHeight-window.ThemeModeCombo.ActualHeight)<.1,"弹出胶囊字体或高度与触发胶囊不一致");
                Require(choice.ActualHeight >= 43.5 && ((SolidColorBrush)choice.Foreground).Color == (Color)window.FindResource("StudioSelectedTextColor"), "菜单选中样式与参考不符");
                foreach(var text in Descendants<TextBlock>(choice))
                    Require(((SolidColorBrush)text.Foreground).Color == (Color)window.FindResource("StudioSelectedTextColor"), $"菜单实际文字未跟随选中色: {text.Text} {text.Foreground}");
                Console.WriteLine($"Popup shape: height={choice.ActualHeight}; surface={((Border)choice.Template.FindName("StudioSurface",choice)).ActualHeight}; radius={((Border)choice.Template.FindName("StudioSurface",choice)).CornerRadius}");
                CaptureElement((FrameworkElement)selectPopup.Child,"dropdown-Light.png");
                window.ThemeModeCombo.IsDropDownOpen = false;
                Require(selectPopup.IsOpen && !selectPopup.Child.IsHitTestVisible, "关闭动画应保留视觉但立即释放点击");
                window.ThemeModeCombo.IsDropDownOpen = true; Pump(220);
                Require(selectPopup.IsOpen && selectPopup.Child.IsHitTestVisible && selectPopup.Child.Opacity > .99, "反向打开下拉菜单失败");
                window.ThemeModeCombo.IsDropDownOpen = false; Pump(220);
                Require(!selectPopup.IsOpen, "关闭后透明弹层未释放");
                window.Left = -10000; window.Top = -10000;
                window.RouterAddressBox.Focus(); Pump(320);
                var focusHalo = (Border)window.RouterAddressBox.Template.FindName("FocusHalo", window.RouterAddressBox);
                Require(Math.Abs(focusHalo.Opacity-.17)<.01, "输入框聚焦光环不正确");
                var inputFace = (Border)window.RouterAddressBox.Template.FindName("StudioSurface",window.RouterAddressBox);
                Require(((SolidColorBrush)inputFace.BorderBrush).Color == (Color)window.FindResource("AccentColor") &&
                    ReferenceEquals(focusHalo.BorderBrush, window.FindResource("AccentBrush")), "输入框聚焦描边与高光必须使用主题蓝色");
                CaptureElement(window.RouterAddressBox,"input-focused-Light.png");
                window.RouterAddressBox.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent}); Pump(320);
                var inputRoot=(FrameworkElement)window.RouterAddressBox.Template.FindName("StudioRoot",window.RouterAddressBox);
                Require(Math.Abs(((ScaleTransform)inputRoot.RenderTransform).ScaleX-.995)<.001,"输入框缺少按压缩放");
                window.RouterAddressBox.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent}); Pump(320);
                Require(Math.Abs(((ScaleTransform)inputRoot.RenderTransform).ScaleX-1)<.001,"输入框按压状态残留");
                Require(!inputRoot.RenderTransform.HasAnimatedProperties, "输入框松开后仍有缩放动画时钟，文字没有回到静止渲染");

                var switchNames = new[] { "StartWithWindowsCheck", "DoNotDisturbCheck", "NotifyIpv6NonNativeCheck", "LanAccessCheck", "NotifyStatusChangeCheck" };
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
                Require(window.DoNotDisturbTimeLabel.Opacity==1 && window.DoNotDisturbTimeSeparator.Opacity==1 &&
                    ((SolidColorBrush)window.DoNotDisturbTimeLabel.Foreground).Color==(Color)window.FindResource("TextColor"),"免打扰启用后整行文字未恢复正常色");
                var knob = (Border)toggle.Template.FindName("PART_Knob", toggle);
                Require(Math.Abs(((TranslateTransform)knob.RenderTransform).X - 20) < 0.1, "开关滑块位置不正确");
                provider.Toggle(); Pump(250);
                Require(toggle.IsChecked == false && !window.DoNotDisturbStartBox.IsEnabled, "关闭后时间编辑未禁用");
                Require(window.DoNotDisturbTimeLabel.Opacity==.45 && window.DoNotDisturbTimeSeparator.Opacity==.45,"禁用时标签和至字状态不一致");
                toggle.Focus(); Pump(50);
                Require(toggle.Template.FindName("FocusRing", toggle) is null, "开关仍含蓝色焦点框");
                window.DoNotDisturbCheck.BringIntoView(); Pump(150);
                Capture(window, "switch-off-focused.png", ThemeService.LightMode);
                Require(window.FindName("LanQrImage") is null, "二维码仍在设置页中");
                window.StartWithWindowsCheck.IsChecked = true;
                window.NotifyIpv6NonNativeCheck.IsChecked = true;
                window.LanAccessCheck.IsChecked = true;
                window.LanAccessDetails.Visibility = Visibility.Visible;
                window.LanAddressBox.Text = "http://10.77.0.100:17863/";
                window.WebAccessStatus.Text = "已开启 · 同一局域网内打开下方链接即可查看。";
                window.UpdateLayout();
                Require(Math.Abs(window.RouterAddressBox.ActualHeight - 44) < 1, "地址输入框高度不应被重复内边距撑高");
                Require(Math.Abs(window.LanAddressBox.ActualHeight - window.CopyLanAddressButton.ActualHeight) < 1, "访问地址与复制按钮高度必须一致");
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                foreach (var size in new[] { (1180d,760d), (980d,650d) })
                {
                    ThemeService.Apply(mode);
                    window.Background = new SolidColorBrush(mode == ThemeService.DarkMode ? Color.FromRgb(30,32,38) : Color.FromRgb(242,243,247));
                    window.Width = size.Item1; window.Height = size.Item2;
                    window.SettingsPanel.ScrollToTop(); Pump(650);
                    var copySurface=(Border)window.CopyLanAddressButton.Template.FindName("StudioSurface",window.CopyLanAddressButton);
                    Require(((SolidColorBrush)copySurface.Background).Color==(Color)window.FindResource("StudioSurfaceColor"),"复制链接按钮底色与表单不统一");
                    Require(((SolidColorBrush)window.CopyLanAddressButton.Foreground).Color==(Color)window.FindResource("StudioTextColor"),"复制链接文字颜色不可读");
                    foreach (var combo in new[]{window.ThemeModeCombo,window.RefreshIntervalCombo})
                    {
                        var toggleSurface=(System.Windows.Controls.Primitives.ToggleButton)combo.Template.FindName("DropDownToggle",combo);
                        var face=(Border)toggleSurface.Template.FindName("StudioSurface",toggleSurface);
                        var rendered=((SolidColorBrush)face.Background).Color;
                        Require(rendered == (Color)window.FindResource("StudioSurfaceColor") || rendered == (Color)window.FindResource("StudioHoverColor") || rendered == (Color)window.FindResource("StudioKeyboardColor"), $"下拉背景未跟随主题: {mode} {rendered}");
                    }
                    VerifySegmentText(segments);
                    var startupKnob = (Border)window.StartWithWindowsCheck.Template.FindName("PART_Knob", window.StartWithWindowsCheck);
                    var startupTrack = (Border)window.StartWithWindowsCheck.Template.FindName("Track", window.StartWithWindowsCheck);
                    Require(((SolidColorBrush)startupTrack.Background).Color == (Color)window.FindResource("SwitchOnColor"), "开启的开关必须跟随当前主题蓝色");
                    window.StartWithWindowsCheck.IsChecked = false; Pump(220);
                    Require(((SolidColorBrush)startupTrack.Background).Color.A is > 0 and < 255, "关闭的开关轨道应透出背景材质");
                    CaptureElement(window.StartWithWindowsCheck, $"switch-off-{mode}.png");
                    Capture(window, $"settings-{mode}-{size.Item1}-switch-off.png", mode);
                    window.StartWithWindowsCheck.IsChecked = true; Pump(220);
                    Require(((SolidColorBrush)startupTrack.Background).Color == (Color)window.FindResource("SwitchOnColor"), "重新开启开关后没有恢复主题色");
                    Require(Math.Abs(((TranslateTransform)startupKnob.RenderTransform).X - 20) < 0.1,
                        $"启用开关位置错误: {((TranslateTransform)startupKnob.RenderTransform).X}");
                    Capture(window, $"settings-{mode}-{size.Item1}-top.png", mode);
                    window.DoNotDisturbStartBox.BringIntoView(); Pump(150);
                    Capture(window, $"settings-{mode}-{size.Item1}-middle.png", mode);
                    window.SettingsPanel.ScrollToBottom(); Pump(250);
                    Capture(window, $"settings-{mode}-{size.Item1}-bottom.png", mode);
                    Require(window.SettingsPanel.ExtentWidth <= window.SettingsPanel.ViewportWidth + 1, "设置页发生横向溢出");
                    Require(window.LanAddressBox.ActualWidth > 180, "局域网链接区域被挤压");
                }
                Console.WriteLine("Apple controls regression PASS: direct ranges, rapid segments, keyboard, five switches, four connection fields, doubled proximity area, reversible dropdown motion, UIA toggle, dependent fields, no QR, light/dark 1180/980 layouts.");
            }
            catch (Exception e) { failure = e; }
            finally { window?.PrepareForExit(); window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is null) return 0;
        Console.Error.WriteLine(failure); return 1;
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var frame=new DispatcherFrame();
        var timer=new DispatcherTimer(DispatcherPriority.Send) { Interval=TimeSpan.FromSeconds(2) };
        EventHandler render=(_,_)=>{if(condition()) frame.Continue=false;};
        timer.Tick+=(_,_)=>frame.Continue=false;
        CompositionTarget.Rendering+=render;timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop();CompositionTarget.Rendering-=render; }
    }

    private static void RenderViewport(RenderTargetBitmap bitmap,FrameworkElement viewport)
    {
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(viewport) {ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,viewport.ActualWidth,viewport.ActualHeight),Stretch=Stretch.Fill},null,new Rect(0,0,viewport.ActualWidth,viewport.ActualHeight));
        bitmap.Render(visual);
    }

    private static void VerifySidebarPolish(MainWindow window)
    {
        KeepTestPopupsOffDesktop(window);
        window.Width=2000;window.Height=760;Pump(200);
        var detailContent=(FrameworkElement)window.DetailPanel.Content;
        Require(detailContent.ActualWidth<=720 && detailContent.ActualWidth>700,"详情内容没有按设置页面限制最大宽度");
        Require(Math.Abs(window.DetailDeviceIconBackground.TranslatePoint(new Point(),detailContent).X)<.1,"标题设备图标仍有额外左缩进");
        Require(((SolidColorBrush)window.CollapsedRouterButton.Foreground).Color==((SolidColorBrush)window.FindResource("MutedTextBrush")).Color &&
            ((SolidColorBrush)window.CollapsedSettingsButton.Foreground).Color==((SolidColorBrush)window.FindResource("MutedTextBrush")).Color,"收起侧栏的底部图标颜色与侧栏按钮不一致");
        window.DetailPanel.ScrollToVerticalOffset(55);Pump(100);
        var detailMask=(FrameworkElement)window.DetailPanel.Template.FindName("DetailViewportMask",window.DetailPanel);
        var detailBitmap=new RenderTargetBitmap((int)Math.Ceiling(detailMask.ActualWidth),(int)Math.Ceiling(detailMask.ActualHeight),96,96,PixelFormats.Pbgra32);RenderViewport(detailBitmap,detailMask);
        var detailPixels=new byte[detailBitmap.PixelWidth*detailBitmap.PixelHeight*4];detailBitmap.CopyPixels(detailPixels,detailBitmap.PixelWidth*4,0);
        Require(Enumerable.Range(0,detailBitmap.PixelWidth).Max(x=>(int)detailPixels[x*4+3])<40,"详情顶部渐隐仍然是硬边");
        Capture(window,"detail-wide-fade.png",ThemeService.LightMode);window.DetailPanel.ScrollToTop();
        window.Width=1180;window.Height=760;Pump(200);
        var listViewer=Descendants<ScrollViewer>(window.DeviceList).First();
        var listBar=(System.Windows.Controls.Primitives.ScrollBar)listViewer.Template.FindName("PART_VerticalScrollBar",listViewer);
        Require(listBar.IsVisible,"测试在线列表未产生滚动条");
        var stationaryX=listBar.TranslatePoint(new Point(),window).X;
        window.DeviceFilterSegments.SelectedIndex=1;Pump(90);
        Console.WriteLine($"Scrollbar outgoing at 90ms opacity={listBar.Opacity:F3}");
        Require(listBar.IsVisible && listBar.Opacity>0 && listBar.Opacity<.9 && Math.Abs(listBar.TranslatePoint(new Point(),window).X-stationaryX)<.1,"滚动条没有同步渐隐或跟随了横移动画");
        Pump(500);Require(!listBar.IsVisible,"无滚动列表仍残留滚动条");
        window.DeviceFilterSegments.SelectedIndex=0;Pump(90);
        var enteringViewer=Descendants<ScrollViewer>(window.DeviceListTransition).First();
        var enteringBar=(System.Windows.Controls.Primitives.ScrollBar)enteringViewer.Template.FindName("PART_VerticalScrollBar",enteringViewer);
        Require(enteringBar.IsVisible && enteringBar.Opacity>0 && enteringBar.Opacity<1,"滚动条出现没有同步渐入");
        Pump(550);Require(listBar.IsVisible && listBar.Opacity==1,"列表切换后滚动条未恢复交互状态");
        var first=window.ConnectionStateInfoBlock.TranslatePoint(new Point(),window.DetailNetworkMetadataRow);
        var last=window.MacInfoBlock.TranslatePoint(new Point(window.MacInfoBlock.ActualWidth,0),window.DetailNetworkMetadataRow);
        Require(Math.Abs(first.X)<1 && Math.Abs(last.X-window.DetailNetworkMetadataRow.ActualWidth)<1,"连接信息没有贴齐内容区两端");
        Require(window.WindowRoot.RowDefinitions[0].ActualHeight==window.MaximizeWindowButton.ActualHeight,"顶栏比窗口按钮多出空白高度");
        Require(window.DetailSurface.TranslatePoint(new Point(),window.WindowRoot).Y==window.MaximizeWindowButton.ActualHeight,"顶栏下方仍有额外留白");
        window.SidebarToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(350);
        window.DeviceSidebar.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});Pump(350);
        Require(window.SidebarExpandedContent.IsVisible,"点击收起侧栏空白区没有展开");
        var scroller=Descendants<ScrollViewer>(window.DeviceList).First();
        scroller.ScrollToVerticalOffset(26);Pump(300);
        var viewport=(FrameworkElement)scroller.Template.FindName("DeviceViewportMask",scroller);
        Console.WriteLine($"Fade diagnostic items={window.DeviceList.Items.Count}, opacity={window.GetDeviceListMotion(window.DeviceList).Opacity}, cache={window.GetDeviceListMotion(window.DeviceList).CacheMode}, filter={window.DeviceFilterSegments.SelectedIndex}, offset={scroller.VerticalOffset}, content={viewport.ActualHeight}");
        Capture(window,"fade-diagnostic.png",ThemeService.LightMode);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(viewport.ActualWidth),(int)Math.Ceiling(viewport.ActualHeight),96,96,PixelFormats.Pbgra32);RenderViewport(bitmap,viewport);
        var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
        int MaxAlpha(int y)=>Enumerable.Range(8,bitmap.PixelWidth-16).Max(x=>(int)pixels[(y*bitmap.PixelWidth+x)*4+3]);
        Console.WriteLine($"FADE alpha edge={MaxAlpha(0)}/{MaxAlpha(bitmap.PixelHeight-1)}, middle={MaxAlpha(40)}");
        Require(MaxAlpha(0)<40 && MaxAlpha(bitmap.PixelHeight-1)<40 && MaxAlpha(40)>100,"渐隐边界仍残留不透明硬切");
        CaptureElement(viewport,"fade-viewport.png");Capture(window,"sidebar-fade.png",ThemeService.LightMode);
        using var json=System.Text.Json.JsonDocument.Parse("""
        {"graph":{"name":"客厅主路由","ip":"10.77.0.1","hardware":"AX3000","onlines":8,"leafs":[
          {"name":"书房节点","ip":"10.77.0.2","hardware":"AX3000","link_type":"wired","onlines":3,"leafs":[
            {"name":"卧室节点","ip":"10.77.0.3","hardware":"AX3000","link_type":"wireless","onlines":2}]},
          {"name":"楼上节点","ip":"10.77.0.4","hardware":"AX3000","link_type":"wireless","onlines":1}]}}
        """);
        var nodes=XiaomiRouterClient.ParseMeshTopology(json.RootElement);
        Require(nodes.Count==4 && nodes[2].ParentNodeId==nodes[1].NodeId && nodes[3].ParentNodeId==nodes[0].NodeId,"Mesh 层级被压平或上联关系错误");
        window.MeshNodesItemsControl.ItemsSource=nodes;window.MeshNodesEmptyText.Visibility=Visibility.Collapsed;
        window.Ipv4AddressText.Text="10.77.0.1";window.Ipv4TypeText.Text="DHCP";window.Ipv6TypeText.Text="Native";
        window.RouterConnectionBadgeText.Text="互联网已连接";window.RouterInfoLoadingBorder.Visibility=Visibility.Collapsed;
        Require(window.Ipv4AddressText.FontSize==window.Ipv4TypeText.FontSize,"IP 地址仍被单独放大");
        foreach(var mode in new[]{ThemeService.LightMode,ThemeService.DarkMode})
        {
            ThemeService.Apply(mode);window.Background=Brushes.Transparent;
            foreach(var width in new[]{1180d,430d,380d})
            {
                window.Width=width;Pump(250);
                typeof(MainWindow).GetMethod("ShowRouterPanel",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);Pump(1000);
                window.RouterPanel.ScrollToTop();Pump(100);
                Capture(window,$"router-fields-{mode}-{width}.png",mode);
                var panel=Descendants<MeshTopologyPanel>(window.MeshNodesItemsControl).Single();
                var containers=Descendants<ContentPresenter>(panel).Where(c=>c.Content is LanDeviceMonitor.Models.MeshNodeInfo).ToArray();
                Require(containers.Length==4,"Mesh 节点未完整显示");
                var x=containers.Select(c=>c.TranslatePoint(new Point(),panel).X).ToArray();
                Require(x[0]<x[1] && x[1]<x[2] && Math.Abs(x[1]-x[3])<.1,"Mesh 缩进未体现真实父子关系");
                CaptureElement(window.MeshNodesItemsControl,$"mesh-{mode}-{width}.png");
                window.RouterPanel.ScrollToBottom();Pump(100);Capture(window,$"router-bottom-{mode}-{width}.png",mode);
            }
        }
        ThemeService.Apply(ThemeService.LightMode);window.Width=430;Pump(100);
        window.CompactBackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(700);
        typeof(MainWindow).GetMethod("OpenCompactDetail",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        Pump(350);Console.WriteLine($"COMPACT at 350ms opacity={window.DetailSurface.Opacity:F3}");
        Require(window.DetailSurface.Opacity>.9,"窄屏进入曲线仍慢于详情的 QuinticEase");Pump(350);
        Require(window.DetailSurface.RenderTransform.Value.IsIdentity,"窄屏动画没有按时结束");
        window.CompactBackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(700);
        typeof(MainWindow).GetMethod("ShowSettingsPanel",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        Require(!window.SettingsPanel.HasAnimatedProperties && window.SettingsPanel.RenderTransform.Value.IsIdentity,"窄屏叠加了第二套页面切换动画");
        Pump(700);Require(window.DetailSurface.RenderTransform.Value.IsIdentity,"窄屏设置入口超出统一动画时长");
        Console.WriteLine("Sidebar/topology PASS: justified metadata, compact title, blank rail click, transparent fade edges, 3-level mesh, IPv4 parity, light/dark and narrow layout.");
    }

    private static void VerifyRestoredWindow(MainWindow window)
    {
        MicaWindowService.Apply(window,false);
        WindowFrameHitTests.Verify(window);
        WindowFrameHitTests.VerifyMaximized(window);Pump(150);
        window.RequestNativeMinimize();Pump(250);
        Require(window.WindowState==WindowState.Minimized,"系统最小化未执行");

        window.WindowState=WindowState.Normal;Pump(300);
        Require(MicaWindowService.LastAcrylicApplySucceeded && window.WindowTransitionSnapshot.Visibility==Visibility.Collapsed,"最小化恢复后材质或覆盖层未恢复");
        var closedToTray=false;
        window.MinimizeToTrayRequested+=(_,_)=>closedToTray=true;
        window.Close();Pump(100);
        Require(closedToTray && window.IsVisible,"原生关闭没有保留关闭到托盘请求");
        CaptureNative(window,"native-caption-restored.png");
        Capture(window,"native-client-restored.png",ThemeService.LightMode);
        Console.WriteLine("Restored dev.208 window PASS: historical caption, system commands, original Acrylic, min/restore and close-to-tray.");
    }

    private static void VerifyRefinements(MainWindow window)
    {
        void Call(string method) => typeof(MainWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        KeepTestPopupsOffDesktop(window);
        WindowFrameHitTests.VerifyMaximized(window);Pump(100);
        window.Width=1180;Pump(200);
        var width=window.SidebarColumn.ActualWidth;
        window.Width=430;Pump(200);
        Call("OpenCompactDetail");Pump(70);
        Require(window.DeviceSidebar.Opacity>0 && window.DeviceSidebar.Opacity<1,"窄屏进入仍然硬切");
        Pump(180);
        Require(window.DetailSurface.IsVisible && window.DetailSurface.Opacity>0 && window.DetailSurface.Opacity<1,"窄屏没有详情进入段");
        Capture(window,"compact-enter-mid.png",ThemeService.LightMode);Pump(500);
        window.CompactBackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(70);
        Require(window.DetailSurface.Opacity>0 && window.DetailSurface.Opacity<1,"窄屏返回仍然硬切");Pump(650);
        window.Width=1180;Pump(200);
        Require(Math.Abs(window.SidebarColumn.ActualWidth-width)<1,"横竖屏往返改变了侧栏宽度");
        window.RouterConnectionBadgeText.Text="互联网已连接";
        window.RouterInfoLoadingBorder.Visibility=Visibility.Collapsed;
        window.Ipv4AddressText.Text="10.77.0.1";window.Ipv4TypeText.Text="DHCP";window.Ipv6TypeText.Text="Native";
        window.WanIpv6AddressText.Text="2001:db8::1";
        foreach(var w in new[]{1180d,1100d,980d,760d,430d,380d})
        {
            window.Width=w;Pump(200);Call("ShowRouterPanel");Pump(700);window.RouterPanel.ScrollToTop();Pump(100);
            var header=window.RouterPageHeader;
            var action=header.Children[1] as FrameworkElement;
            var point=action!.TranslatePoint(new Point(),header);
            Require(point.X>=-.1 && point.X+action.ActualWidth<=header.ActualWidth+.1,"路由器状态或刷新越界");
            Require(window.RouterConnectionBadge.ActualWidth>=window.RouterConnectionBadgeText.ActualWidth+20,"连接状态胶囊挤压文字");
            if(header.ActualWidth>=470) Require(point.Y<20,"足够宽时标题栏提前换行");
            CaptureElement(window.RouterPageHeader,$"router-header-{w}.png");
            window.RouterPanel.ScrollToBottom();Pump(80);Capture(window,$"router-restart-{w}.png",ThemeService.LightMode);window.RouterPanel.ScrollToTop();
        }
        window.Width=1180;Call("ShowSettingsPanel");Pump(700);
        window.RouterPasswordBox.Password="sample-only";window.SettingsPanel.ScrollToTop();Pump(120);
        CaptureElement(window.RouterPasswordBox,"password-entered.png");
        var passwordHost=(FrameworkElement)window.RouterPasswordBox.Template.FindName("PART_ContentHost",window.RouterPasswordBox);
        Require(passwordHost.RenderTransform is TranslateTransform,"密码未进行字形轴对齐");
        window.RouterPasswordBox.Clear();
        window.SidebarToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(350);
        Require(window.CollapsedRouterButton.Template.FindName("IconHover",window.CollapsedRouterButton) is Border,"收起导航不是轻量图标样式");
        Capture(window,"collapsed-lightweight.png",ThemeService.LightMode);
        ExportDeviceIconAtlas(window);
        Console.WriteLine("Refinement PASS: native maximize, compact two-stage frames, width roundtrip, 6 router widths, restart layout, password glyph alignment, lightweight rail.");
    }

    private static void VerifyResponsiveNavigation(MainWindow window)
    {
        void Navigate(string method) => typeof(MainWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        KeepTestPopupsOffDesktop(window);
        window.Width=1180;window.Height=760;Pump(200);
        window.DoNotDisturbCheck.IsChecked=false;Pump(30);
        Require(window.DoNotDisturbTimeRow.Visibility==Visibility.Collapsed,"关闭免打扰后时段没有隐藏");
        window.DoNotDisturbCheck.IsChecked=true;Pump(30);
        Require(window.DoNotDisturbTimeRow.Visibility==Visibility.Visible,"开启免打扰后时段未显示");
        foreach(ComboBoxItem interval in window.RefreshIntervalCombo.Items)
            Require(interval.Content.ToString()==interval.Tag+"\u2009秒","刷新间隔缺少单位或数值标签");
        window.RefreshIntervalCombo.SelectedIndex=0; window.ThemeModeCombo.SelectedIndex=0;
        window.DeviceFilterSegments.SelectedIndex=1;Pump(90);
        Require(window.DeviceList.RenderTransform.Value.IsIdentity && window.DeviceListTransition.RenderTransform.Value.IsIdentity,"滚动条仍与整张列表平移");
        Require(window.GetDeviceListMotion(window.DeviceListTransition).RenderTransform is TranslateTransform,"列表内容没有执行切换动画");
        Pump(700);
        var row=(ListBoxItem)window.DeviceList.ItemContainerGenerator.ContainerFromIndex(0);
        var icon=Descendants<DeviceTypeIcon>(row).First();
        Require(((SolidColorBrush)icon.IconBrush).Color==((SolidColorBrush)window.FindResource("MutedTextBrush")).Color,"离线设备图标不是灰色");
        window.DeviceFilterSegments.SelectedIndex=0;Pump(800);
        var scroller=Descendants<ScrollViewer>(window.DeviceList).First();
        scroller.ScrollToTop();Pump(40);
        var bar=Descendants<System.Windows.Controls.Primitives.ScrollBar>(scroller).First(b=>b.Orientation==Orientation.Vertical);
        Require(bar.Margin.Top==0 && bar.Margin.Bottom==5,"滚动条仍有额外上下空隙");
        scroller.ScrollToVerticalOffset(30);Pump(50);
        var viewport=(FrameworkElement)scroller.Template.FindName("DeviceViewportMask",scroller);
        Require(viewport.OpacityMask is LinearGradientBrush,"列表边界缺少渐隐");
        window.SidebarToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(350);
        Require(window.CollapsedRouterButton.IsVisible && window.CollapsedSettingsButton.IsVisible,"收起侧栏缺少圆按钮");
        Require(!Descendants<System.Windows.Shapes.Ellipse>(window.CollapsedSidebarContent).Any(),"收起侧栏仍有状态圆点");
        Capture(window,"collapsed-234.png",ThemeService.LightMode);
        window.CollapsedSidebarButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(350);
        window.Ipv4AddressText.Text="10.77.0.100";window.Ipv4TypeText.Text="DHCP";
        window.WanIpv6AddressText.Text="2001:db8::1";
        window.Ipv6TypeText.Text="Native";window.RouterInfoLoadingBorder.Visibility=Visibility.Collapsed;
        foreach(var width in new[]{1180d,1132d,760d,430d,380d})
        {
            window.Width=width;Pump(300);
            if(width<900)
            {
                window.CompactBackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(700);
                Require(window.DeviceSidebar.IsVisible && !window.DetailSurface.IsVisible,"窄窗口没有只显示设备列表");
                Require(window.DeviceSidebar.ActualWidth>width-40,"窄窗口设备列表没有填满宽度");
                Capture(window,$"compact-list-{width}.png",ThemeService.LightMode);
                var tappedRow=(ListBoxItem)window.DeviceList.ItemContainerGenerator.ContainerFromIndex(2);
                tappedRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left)
                    { RoutedEvent=Mouse.PreviewMouseDownEvent });
                tappedRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=Mouse.PreviewMouseUpEvent });
                Pump(700);
                Require(!window.DeviceSidebar.IsVisible && window.DetailSurface.IsVisible && window.CompactBackButton.IsVisible,"窄窗口详情或返回按钮缺失");
                Require(ReferenceEquals(window.SelectedDevice,window.DeviceList.Items[2]),"点击胶囊后显示了错误的设备");
            }
            window.DetailPanel.ScrollToTop();Pump(100);Capture(window,$"compact-detail-{width}.png",ThemeService.LightMode);
            window.DetailPanel.ScrollToBottom();Pump(150);Capture(window,$"compact-device-settings-{width}.png",ThemeService.LightMode);
            Navigate("ShowSettingsPanel");Pump(700);window.SettingsPanel.ScrollToTop();Pump(100);Capture(window,$"compact-settings-{width}.png",ThemeService.LightMode);
            window.SettingsPanel.ScrollToBottom();Pump(100);Capture(window,$"compact-settings-bottom-{width}.png",ThemeService.LightMode);
            Navigate("ShowRouterPanel");Pump(700);Capture(window,$"router-{width}.png",ThemeService.LightMode);
            typeof(MainWindow).GetMethod("ShowDevicesPanel",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[false,false]);Pump(100);
        }
        window.Width=1180;Pump(250);
        Require(window.DeviceSidebar.IsVisible && window.DetailSurface.IsVisible && !window.CompactBackButton.IsVisible,"放宽后未恢复双栏");
        Console.WriteLine("Responsive navigation PASS: 1180/760/430/380, list/detail/back, stable scrollbar, gray offline icons, DND visibility, refresh units, collapsed buttons, wide restoration.");
    }

    private static void ProfileDeviceTransitions(MainWindow window)
    {
        Console.WriteLine($"HISTORY visuals={Descendants<TextBlock>(window.SessionHistoryItems).Count()}, models={window.DetailSessionItems.Count}, extent={window.DetailPanel.ScrollableHeight:F1}");
        Require(window.DetailSessionItems.Count==100 && Descendants<TextBlock>(window.SessionHistoryItems).Count()<140,"不可见历史记录仍全部创建了控件");
        for(var run=0;run<4;run++)
        {
            var clock=System.Diagnostics.Stopwatch.StartNew();var frames=new List<double>();
            EventHandler tick=(_,_)=>frames.Add(clock.Elapsed.TotalMilliseconds);
            CompositionTarget.Rendering+=tick;
            window.DeviceFilterSegments.SelectedIndex=(run+1)%2;Pump(800);
            CompositionTarget.Rendering-=tick;
            var gaps=frames.Zip(frames.Skip(1),(a,b)=>(Start:a,Gap:b-a)).Where(p=>p.Start<650).ToArray();
            Console.WriteLine($"PERF run={run} frames={frames.Count} maxGap={gaps.Max(p=>p.Gap):F1} gapsOver25={string.Join(';',gaps.Where(p=>p.Gap>25).Select(p=>$"{p.Start:F0}:{p.Gap:F1}"))}");
        }
        window.DetailPanel.ScrollToVerticalOffset(window.DetailPanel.ScrollableHeight/2);Pump(250);
        Require(window.SessionHistoryItems.ItemContainerGenerator.ContainerFromIndex(50) is not null,"历史中部记录没有按需呈现");
        window.DetailPanel.ScrollToBottom();Pump(250);
        Require(window.SessionHistoryItems.ItemContainerGenerator.ContainerFromIndex(99) is not null,"最后一条历史记录不可访问");
        var fullHeight=window.SessionHistoryItems.ActualHeight;
        var selected=window.SelectedDevice!;
        var fewer=new LanDeviceMonitor.ViewModels.DeviceItemViewModel(new LanDeviceMonitor.Models.DeviceViewData
        { Profile=selected.Data.Profile, Identity=selected.Data.Identity, Sessions=selected.Data.Sessions.Take(5).ToArray() });
        typeof(MainWindow).GetMethod("UpdateSelectedDevice",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[fewer,true]);Pump(250);
        Require(window.SessionHistoryItems.ActualHeight<fullHeight/10 && window.SessionHistoryItems.ActualHeight>0,"切换到少量历史后仍保留空白长尾");
        Console.WriteLine("History viewport regression PASS: full 100 records retained, middle/end reachable, short history extent correct.");
    }

    private static void KeepTestPopupsOffDesktop(MainWindow window)
    {
        foreach(var combo in new[]{window.DeviceTypeCombo,window.ThemeModeCombo,window.RefreshIntervalCombo})
        {
            combo.ApplyTemplate();
            if(combo.Template.FindName("PART_Popup",combo) is not System.Windows.Controls.Primitives.Popup popup) continue;
            popup.Opened += (_,_) =>
            {
                if(System.Windows.Interop.HwndSource.FromVisual(popup.Child) is System.Windows.Interop.HwndSource source)
                {
                    // Keep opacity, DPI, layout, animations and drawing intact. Only
                    // move the test popup HWND; real placement has its separate check.
                    SetWindowPos(source.Handle,IntPtr.Zero,-20000,-20000,0,0,0x0001|0x0004|0x0010);
                }
            };
        }
    }

    private static void VerifyPopupAlignment(MainWindow window)
    {
        typeof(MainWindow).GetMethod("ShowSettingsPanel",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        window.Left=80;window.Top=80;window.ThemeModeCombo.SelectedIndex=0;Pump(600);
        var combo=window.ThemeModeCombo;
        var triggerLabel=Descendants<TextBlock>(combo).First(t=>t.Text=="跟随系统");
        var popup=(System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup",combo);
        // Only screen coordinates are under test here. Opacity does not change
        // layout/placement; the normal animation is tested separately offscreen.
        popup.Opened += (_,_) => { popup.Child.BeginAnimation(UIElement.OpacityProperty,null); popup.Child.Opacity=0; };
        combo.IsDropDownOpen=true;Pump(650);
        var border=(Border)popup.Child;
        var item=(ComboBoxItem)combo.Items[0];
        var text=Descendants<TextBlock>(item).First(t=>t.Text=="跟随系统");
        Console.WriteLine($"POPUP diagnostic target={combo.PointToScreen(new Point()).X}, surface={border.PointToScreen(new Point()).X}, trigger={triggerLabel.PointToScreen(new Point()).X}, text={text.PointToScreen(new Point()).X}, item={item.PointToScreen(new Point()).X}, itemPadding={item.Padding}, surfacePadding={border.Padding}, width={border.ActualWidth}");
        CaptureElement((FrameworkElement)popup.Child,"popup-content-width.png");
        Require(Math.Abs(text.PointToScreen(new Point()).X-triggerLabel.PointToScreen(new Point()).X)<=1,"弹层文字屏幕对齐失败");
        Require(item.ActualWidth >= combo.ActualWidth-1,"菜单高亮胶囊不应窄于触发胶囊");
        Require(Math.Abs(text.TranslatePoint(new Point(),item).X-triggerLabel.TranslatePoint(new Point(),combo).X)<1,"高亮胶囊内部文字留白与触发框不一致");
        var face=(Border)item.Template.FindName("StudioSurface",item);
        Require(Math.Abs(face.CornerRadius.TopLeft-item.ActualHeight/2)<.1,"下拉选中项丢失胶囊圆角");
        Require(((SolidColorBrush)item.Foreground).Color==(Color)window.FindResource("AccentColor"),"下拉选中文本没有使用主题色");
        foreach(var mode in new[]{ThemeService.DarkMode,ThemeService.LightMode})
        {
            combo.IsDropDownOpen=false;Pump(250);ThemeService.Apply(mode);
            combo.IsDropDownOpen=true;Pump(600);
            Require(Math.Abs(face.CornerRadius.TopLeft-item.ActualHeight/2)<.1,"主题切换或重新打开后胶囊圆角丢失");
        }
        combo.IsDropDownOpen=false;Pump(250);
        Console.WriteLine("Popup alignment regression PASS: label alignment, accent selection, capsule shape after reopen/theme changes.");
    }

    private static void ExportDeviceIconAtlas(MainWindow window)
    {
        var panel=new System.Windows.Controls.WrapPanel { Width=400, Background=Brushes.White };
        foreach(var type in DeviceIdentityService.AvailableTypes)
        {
            var stack=new StackPanel { Width=100, Height=86 };
            stack.Children.Add(new DeviceTypeIcon { DeviceType=type.TypeName,Width=32,Height=32,Margin=new Thickness(0,12,0,8),IconBrush=(Brush)window.FindResource("AccentBrush") });
            stack.Children.Add(new TextBlock { Text=type.TypeName,FontFamily=(FontFamily)window.FindResource("AppTextFont"),FontSize=12,HorizontalAlignment=HorizontalAlignment.Center });
            panel.Children.Add(stack);
        }
        panel.Measure(new Size(400,1000));panel.Arrange(new Rect(0,0,400,panel.DesiredSize.Height));panel.UpdateLayout();
        CaptureElement(panel,"device-icon-atlas.png");
    }

    private static void VerifySegmentText(SegmentedControl segments)
    {
        segments.UpdateLayout();
        foreach (ListBoxItem item in segments.Items)
        {
            var expected = ((SolidColorBrush)item.Foreground).Color;
            var themeTarget = (SolidColorBrush)(item.IsSelected ? ((SegmentItem)item).ActiveBrush : ((SegmentItem)item).InactiveBrush);
            Require(expected == themeTarget.Color, $"分段文字没有跟随主题: actual={expected}, target={themeTarget.Color}");
            foreach (var text in Descendants<TextBlock>(item))
            {
                Require(text.Foreground is SolidColorBrush brush && brush.Color == expected,
                    $"分段文字颜色被全局样式覆盖: {text.Text}");
                Require(text.FontWeight == FontWeights.Normal, "分段标签或计数不应额外加粗");
            }
        }
        var selected = (ListBoxItem)segments.Items[segments.SelectedIndex];
        var other = (ListBoxItem)segments.Items[(segments.SelectedIndex + 1) % segments.Items.Count];
        Require(((SolidColorBrush)selected.Foreground).Color != ((SolidColorBrush)other.Foreground).Color,
            $"选中和未选中文字必须有不同颜色: selected={selected.IsSelected}/{selected.IsMouseOver}/{((SolidColorBrush)selected.Foreground).Color}, other={other.IsSelected}/{other.IsMouseOver}/{((SolidColorBrush)other.Foreground).Color}, target={((SolidColorBrush)((SegmentItem)other).InactiveBrush).Color}, own={((SolidColorBrush)typeof(SegmentItem).GetField("_textBrush", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(other)!).Color}, ref={ReferenceEquals(other.Foreground, typeof(SegmentItem).GetField("_textBrush", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(other))}");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }


    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static void CaptureElement(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var exportedControls=Descendants<System.Windows.Controls.Control>(element).Select(c=>(Control:c,Brush:c.Foreground)).ToArray();
        var exportedText=Descendants<TextBlock>(element).Select(c=>(Text:c,Brush:c.Foreground)).ToArray();
        foreach(var c in exportedControls) c.Control.SetCurrentValue(Control.ForegroundProperty,c.Brush.CloneCurrentValue());
        foreach(var c in exportedText) c.Text.SetCurrentValue(TextBlock.ForegroundProperty,c.Brush.CloneCurrentValue());
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()) { dc.DrawRectangle(new SolidColorBrush(ThemeService.ResolveIsDark(ThemeService.CurrentMode) ? Color.FromRgb(30,32,38) : Colors.White),null,new Rect(0,0,element.ActualWidth+12,element.ActualHeight+12)); dc.DrawRectangle(new VisualBrush(element),null,new Rect(6,6,element.ActualWidth,element.ActualHeight)); }
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling((element.ActualWidth+12)*2),(int)Math.Ceiling((element.ActualHeight+12)*2),192,192,PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(Path.Combine(Environment.CurrentDirectory,".tools","ui-dev238",name));encoder.Save(file);
        foreach(var c in exportedControls) c.Control.SetCurrentValue(Control.ForegroundProperty,c.Brush);
        foreach(var c in exportedText) c.Text.SetCurrentValue(TextBlock.ForegroundProperty,c.Brush);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr handle, IntPtr dc, int flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left,Top,Right,Bottom; }
    private static void CaptureNative(Window window, string name)
    {
        var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
        GetWindowRect(handle,out var rect);
        using var bitmap=new System.Drawing.Bitmap(rect.Right-rect.Left,rect.Bottom-rect.Top);
        using var graphics=System.Drawing.Graphics.FromImage(bitmap);
        var dc=graphics.GetHdc();
        try { Require(PrintWindow(handle,dc,2),"原生窗口截图失败"); } finally { graphics.ReleaseHdc(dc); }
        bitmap.Save(Path.Combine(Environment.CurrentDirectory,".tools","ui-dev238",name),System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void Capture(MainWindow window, string name, string mode)
    {
        window.UpdateLayout();
        if (name.StartsWith("devices-") || name.StartsWith("settings-")) FontVisualProof.Capture(window, name);
        if (name == "settings-Dark-980-top.png") CaptureNative(window,"settings-Dark-native.png");
        // Offscreen WPF exports can paint an animation's cached initial transform.
        // Snapshot its current value for the export, then restore the live clock.
        var transforms = Descendants<UIElement>(window)
            .Where(element => element.RenderTransform.HasAnimatedProperties)
            .Select(element => (Element: element, Transform: element.RenderTransform)).ToArray();
        foreach (var pair in transforms) pair.Element.RenderTransform = pair.Transform.CloneCurrentValue();
        var textBrushes = Descendants<SegmentItem>(window)
            .Select(item => (Item: item, Brush: item.Foreground)).ToArray();
        foreach (var pair in textBrushes) pair.Item.Foreground = pair.Brush.CloneCurrentValue();
        var width = (int)window.ActualWidth; var height = (int)window.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(mode == ThemeService.DarkMode ? Color.FromRgb(30,32,38) : Color.FromRgb(242,243,247)), null, new Rect(0,0,width,height));
            dc.DrawRectangle(new VisualBrush(window) { ViewboxUnits=BrushMappingMode.Absolute, Viewbox=new Rect(0,0,width,height) }, null, new Rect(0,0,width,height));
        }
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width*dpi.DpiScaleX),(int)Math.Ceiling(height*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(Environment.CurrentDirectory, ".tools", "ui-dev238");
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory,name)); encoder.Save(file);
        foreach (var pair in transforms) pair.Element.RenderTransform = pair.Transform;
        foreach (var pair in textBrushes) pair.Item.Foreground = pair.Brush;
    }
}
