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
                var devices = Enumerable.Range(0, 12).Select(index => index < 10).Select((online, index) => new LanDeviceMonitor.Models.DeviceViewData
                {
                    Profile = new LanDeviceMonitor.Models.DeviceProfile
                    {
                        MacAddress = $"00:00:00:00:30:{index + 1:X2}", LastRouterName = index == 0 ? "测试笔记本" : $"测试设备 {index + 1}",
                        LastKnownOnline = online, LastSeenAt = DateTimeOffset.Now, FirstSeenAt = DateTimeOffset.Now,
                        LastIpAddress = $"192.168.31.{100 + index}",
                        LastDownloadSpeedBytes = index == 9 ? 999 * 1024 * 1024 : index * 1024
                    },
                    Identity = new LanDeviceMonitor.Models.DeviceIdentity("电脑", "\uE770", index == 0 ? "测试笔记本" : $"测试设备 {index + 1}"),
                    Sessions = new[] { 62, 1550 }.Select(minutes => new LanDeviceMonitor.Models.DeviceSession
                    {
                        MacAddress = $"00:00:00:00:30:{index + 1:X2}",
                        OnlineAt = DateTimeOffset.Now.AddMinutes(-minutes - 10),
                        OfflineAt = DateTimeOffset.Now.AddMinutes(-10)
                    }).ToList()
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
                Require(Grid.GetColumnSpan(window.WorkspaceSurface) == 3 && window.DeviceSidebar.BorderThickness == new Thickness(0) &&
                    window.DetailSurface.BorderThickness == new Thickness(0), "设备与详情应共用一个外部内容容器");
                Require(window.FindName("NavigationRail") is null && Descendants<TextBlock>(window.SidebarExpandedContent).Any(text => text.Text == "设置") &&
                    Descendants<TextBlock>(window.SidebarRailRouterButton).Any(text => text.Text == "路由器"), "导航应合并到设备侧栏上方");
                var copied = new List<string>();
                window.CopyNetworkText = copied.Add;
                window.CopyDeviceIpButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                window.CopyDeviceMacButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(copied.SequenceEqual(new[] { window.SelectedDevice!.IpAddress, window.SelectedDevice.MacAddress }), "地址复制内容错误");
                Pump(1250);
                window.SidebarColumn.Width = new GridLength(310); window.UpdateLayout();
                var selectedBeforeCollapse = window.SelectedDevice;
                window.SidebarToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(70);
                var intermediateWidth = window.SidebarColumn.ActualWidth;
                if (SystemParameters.ClientAreaAnimation)
                    Require(intermediateWidth > MainWindow.CollapsedSidebarWidth && intermediateWidth < 310, "侧栏收起必须存在动画中间帧");
                Capture(window, "sidebar-mid-transition.png", ThemeService.LightMode);
                window.CollapsedSidebarButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                window.UpdateLayout();
                Require(Math.Abs(window.SidebarColumn.ActualWidth - intermediateWidth) < 2 || !SystemParameters.ClientAreaAnimation,
                    "侧栏反向动画发生跳变");
                Pump(350);
                Require(Math.Abs(window.SidebarColumn.ActualWidth - 310) < 1, "反向展开没有恢复侧栏宽度");
                window.SidebarToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump(350);
                Require(window.SidebarColumn.ActualWidth == MainWindow.CollapsedSidebarWidth && window.SidebarExpandedContent.Visibility == Visibility.Collapsed &&
                    window.CollapsedSidebarButton.IsVisible && !window.SidebarRailSettingsButton.IsVisible, "收起后应仅保留统一窄栏");
                Require(window.SidebarDividerLine.IsVisible && window.SidebarDividerColumn.ActualWidth + 0.001 >= 1 / VisualTreeHelper.GetDpi(window).DpiScaleX, $"收起侧栏缺少分界线 width={window.SidebarDividerColumn.ActualWidth} line={window.SidebarDividerLine.ActualWidth} visible={window.SidebarDividerLine.IsVisible}");
                Capture(window, "sidebar-collapsed.png", ThemeService.LightMode);
                window.CopyDeviceIpButton.Focus();
                Pump(220);
                window.CollapsedSidebarButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                Require(window.CollapsedSidebarGlyph.Opacity == 0, $"悬停图标切换应延迟，避免掠过时闪烁 opacity={window.CollapsedSidebarGlyph.Opacity} focus={Keyboard.FocusedElement}");
                Pump(380);
                Require(window.CollapsedSidebarGlyph.Opacity == 1 && window.CollapsedAppIcon.Opacity == 0, "悬停未将软件图标切换为展开图标");
                Capture(window, "sidebar-collapsed-hover.png", ThemeService.LightMode);
                window.CollapsedSidebarButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
                Require(window.CollapsedAppIcon.Opacity < 1 || !SystemParameters.ClientAreaAnimation, "图标恢复不应硬切");
                Pump(200);
                Require(window.CollapsedAppIcon.Opacity == 1, "移出后软件图标未恢复");
                var blankHit = window.CollapsedSidebarButton.InputHitTest(new Point(28, window.CollapsedSidebarButton.ActualHeight * 0.7));
                Require(blankHit is not null && window.CollapsedSidebarButton.ActualHeight > 300, "收起窄栏的空白区域必须可点击");
                var expandPeer = UIElementAutomationPeer.CreatePeerForElement(window.CollapsedSidebarButton)!;
                ((IInvokeProvider)expandPeer.GetPattern(PatternInterface.Invoke)!).Invoke(); Pump(350);
                Require(Math.Abs(window.SidebarColumn.ActualWidth - 310) < 1 && ReferenceEquals(selectedBeforeCollapse, window.SelectedDevice), "点击窄栏应展开并保留选中设备");
                window.SidebarRailSettingsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                typeof(MainWindow).GetField("_isPopulatingAppSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true); Pump(550);
                Require(window.SettingsPanel.Visibility == Visibility.Visible, "合并后的设置入口失效");
                window.SidebarRailDevicesButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump(550);
                Require(window.DetailPanel.Visibility == Visibility.Visible, "合并导航无法返回设备详情");
                window.SidebarColumn.Width = new GridLength(390); window.UpdateLayout();
                Capture(window, "devices-Light.png", ThemeService.LightMode);
                Require(window.DetailConnectionText.Text == window.SelectedDevice!.ConnectionText &&
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
                Require(Math.Abs(window.ConnectionStatusText.TranslatePoint(new Point(), window).Y - window.LastRefreshText.TranslatePoint(new Point(), window).Y) < 1, "连接信息未在同一行");
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
                Require(window.WindowRoot.RowDefinitions.Count == 0, "应用顶栏仍占据独立行");
                Require(deviceItem.ActualHeight >= 56 && deviceItem.ActualHeight <= 58, "单行设备胶囊没有压缩高度");
                Require(!Descendants<TextBlock>(deviceItem).Any(text => text.Text is "在线" or "离线" or "网线接入"),
                    "左侧设备条目仍含状态或连接信息");
                window.Width = 980; window.Height = 650; Pump(250);
                Require(window.SidebarColumn.ActualWidth <= window.WorkspaceGrid.ActualWidth - 605, "窄窗口没有限制侧栏宽度");
                Require(window.DetailNetworkMetadataRow.Children.OfType<StackPanel>().Count() == 3, "网络信息未横向排列");
                Capture(window, "devices-Light-980.png", ThemeService.LightMode);
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
                toggle.Focus(); Pump(50);
                Require(toggle.Template.FindName("FocusRing", toggle) is null, "开关仍含蓝色焦点框");
                window.DoNotDisturbCheck.BringIntoView(); Pump(150);
                Capture(window, "switch-off-focused.png", ThemeService.LightMode);
                Require(window.FindName("LanQrImage") is null, "二维码仍在设置页中");
                window.StartWithWindowsCheck.IsChecked = true;
                window.EmergencyHotKeyEnabledCheck.IsChecked = true;
                window.NotifyIpv6NonNativeCheck.IsChecked = true;
                window.LanAccessCheck.IsChecked = true;
                window.LanAccessDetails.Visibility = Visibility.Visible;
                window.LanAddressBox.Text = "http://10.77.0.100:17863/";
                window.WebAccessStatus.Text = "已开启 · 同一局域网内打开下方链接即可查看。";
                window.UpdateLayout();
                Require(Math.Abs(window.RouterAddressBox.ActualHeight - 40) < 1, "地址输入框高度不应被重复内边距撑高");
                Require(Math.Abs(window.LanAddressBox.ActualHeight - window.CopyLanAddressButton.ActualHeight) < 1, "访问地址与复制按钮高度必须一致");
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode })
                foreach (var size in new[] { (1180d,760d), (980d,650d) })
                {
                    ThemeService.Apply(mode);
                    window.Background = new SolidColorBrush(mode == ThemeService.DarkMode ? Color.FromRgb(30,32,38) : Color.FromRgb(242,243,247));
                    window.Width = size.Item1; window.Height = size.Item2;
                    window.SettingsPanel.ScrollToTop(); Pump(250);
                    VerifySegmentText(segments);
                    var startupKnob = (Border)window.StartWithWindowsCheck.Template.FindName("PART_Knob", window.StartWithWindowsCheck);
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

    private static void Capture(MainWindow window, string name, string mode)
    {
        window.UpdateLayout();
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
            dc.DrawRectangle(new VisualBrush(window), null, new Rect(0,0,width,height));
        }
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width*dpi.DpiScaleX),(int)Math.Ceiling(height*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(Environment.CurrentDirectory, ".tools", "ui-dev209");
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory,name)); encoder.Save(file);
        foreach (var pair in transforms) pair.Element.RenderTransform = pair.Transform;
        foreach (var pair in textBrushes) pair.Item.Foreground = pair.Brush;
    }
}
