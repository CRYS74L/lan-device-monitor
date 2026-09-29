using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace LanDeviceMonitor;

public partial class MainWindow
{
    private double _expandedSidebarWidth = 390;
    private bool _sidebarCollapsed;
    private bool _sidebarAnimating;
    private int _sidebarAnimationGeneration;
    internal Action<string> CopyNetworkText { get; set; } = System.Windows.Clipboard.SetText;
    private static readonly DependencyProperty SidebarAnimationWidthProperty = DependencyProperty.Register(
        "SidebarAnimationWidth", typeof(double), typeof(MainWindow), new PropertyMetadata(390d, (owner, args) =>
        {
            var window = (MainWindow)owner;
            if (window.SidebarColumn is null) return;
            var width = Math.Max(0, (double)args.NewValue);
            window.SidebarColumn.Width = new GridLength(width);
            window.SidebarDividerColumn.Width = new GridLength(Math.Min(6, width * 6 / 280));
        }));

    private double MaximumSidebarWidth => Math.Max(280, Math.Min(520, WorkspaceGrid.ActualWidth - 64 - 606));

    private void WorkspaceGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SidebarColumn is null || SidebarExpandedContent is null || WorkspaceGrid.ActualWidth <= 0) return;
        if (_sidebarAnimating) { ApplySidebarState(false); return; }
        if (_sidebarCollapsed) return;
        SidebarColumn.MaxWidth = MaximumSidebarWidth;
        if (SidebarColumn.Width.Value > SidebarColumn.MaxWidth)
            SidebarColumn.Width = new GridLength(SidebarColumn.MaxWidth);
    }

    private void DeviceSidebar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_sidebarAnimating && !_sidebarCollapsed && SidebarExpandedContent is not null)
            SidebarExpandedContent.Width = Math.Max(0, DeviceSidebar.ActualWidth - 38);
    }

    private void SidebarSplitter_DragCompleted(object sender, DragCompletedEventArgs e) =>
        _expandedSidebarWidth = SidebarColumn.ActualWidth;

    private void SidebarToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_sidebarCollapsed && !_sidebarAnimating) _expandedSidebarWidth = SidebarColumn.ActualWidth;
        _sidebarCollapsed = !_sidebarCollapsed;
        ApplySidebarState(SystemParameters.ClientAreaAnimation && IsLoaded);
    }

    private void ApplySidebarState(bool animate)
    {
        var currentWidth = _sidebarAnimating ? (double)GetValue(SidebarAnimationWidthProperty) : SidebarColumn.ActualWidth;
        var currentOpacity = SidebarExpandedContent.Opacity;
        var targetWidth = _sidebarCollapsed ? 0 : Math.Clamp(_expandedSidebarWidth, 280, MaximumSidebarWidth);
        var generation = ++_sidebarAnimationGeneration;
        BeginAnimation(SidebarAnimationWidthProperty, null);
        SidebarExpandedContent.BeginAnimation(OpacityProperty, null);
        _sidebarAnimating = animate && Math.Abs(currentWidth - targetWidth) > 0.1;
        SidebarColumn.MinWidth = 0;
        SidebarColumn.MaxWidth = MaximumSidebarWidth;
        DeviceSidebar.Visibility = Visibility.Visible;
        SidebarSplitter.Visibility = Visibility.Visible;
        SidebarSplitter.IsEnabled = !_sidebarAnimating && !_sidebarCollapsed;
        SidebarExpandedContent.IsHitTestVisible = !_sidebarCollapsed;
        SidebarExpandedContent.Width = Math.Max(0, Math.Clamp(_expandedSidebarWidth, 280, MaximumSidebarWidth) - 46);
        SidebarToggleButton.ToolTip = _sidebarCollapsed ? "展开设备栏" : "收起设备栏";
        System.Windows.Automation.AutomationProperties.SetName(SidebarToggleButton, _sidebarCollapsed ? "展开设备栏" : "收起设备栏");

        void Finish()
        {
            if (generation != _sidebarAnimationGeneration) return;
            BeginAnimation(SidebarAnimationWidthProperty, null);
            SetValue(SidebarAnimationWidthProperty, targetWidth);
            SidebarColumn.Width = new GridLength(targetWidth);
            SidebarDividerColumn.Width = new GridLength(_sidebarCollapsed ? 0 : 6);
            SidebarExpandedContent.BeginAnimation(OpacityProperty, null);
            SidebarExpandedContent.Opacity = _sidebarCollapsed ? 0 : 1;
            _sidebarAnimating = false;
            DeviceSidebar.Visibility = SidebarSplitter.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            SidebarColumn.MinWidth = _sidebarCollapsed ? 0 : 280;
            SidebarColumn.MaxWidth = _sidebarCollapsed ? 0 : MaximumSidebarWidth;
            SidebarSplitter.IsEnabled = !_sidebarCollapsed;
            if (!_sidebarCollapsed) SidebarExpandedContent.Width = Math.Max(0, targetWidth - 46);
        }

        if (!_sidebarAnimating) { Finish(); return; }
        SetValue(SidebarAnimationWidthProperty, currentWidth);
        SidebarExpandedContent.Opacity = currentOpacity;
        var widthAnimation = new System.Windows.Media.Animation.DoubleAnimation(currentWidth, targetWidth, TimeSpan.FromMilliseconds(260))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        widthAnimation.Completed += (_, _) => Finish();
        BeginAnimation(SidebarAnimationWidthProperty, widthAnimation);
        SidebarExpandedContent.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(currentOpacity, _sidebarCollapsed ? 0 : 1, TimeSpan.FromMilliseconds(160)));
    }

    private void SidebarDevicesButton_Click(object sender, RoutedEventArgs e) => ShowDevicesPanel(animate: true);

    private void UpdateSidebarNavigation()
    {
        foreach (var button in new[] { SidebarRailDevicesButton, SidebarRailRouterButton, SidebarRailSettingsButton })
            button.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        var selected = _showingSettings ? SidebarRailSettingsButton : _showingRouter ? SidebarRailRouterButton : SidebarRailDevicesButton;
        selected.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "QuietControlBrush");
    }

    private void CopyNetworkAddress_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || SelectedDevice is null) return;
        var value = button.Tag as string == "IP" ? SelectedDevice.IpAddress : SelectedDevice.MacAddress;
        if (string.IsNullOrWhiteSpace(value) || value == "—") return;
        var feedback = "已复制";
        try
        {
            CopyNetworkText(value);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            feedback = "剪贴板正忙，请重试";
        }
        var tip = new System.Windows.Controls.ToolTip { Content = feedback, PlacementTarget = button, IsOpen = true, StaysOpen = true };
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, button.Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(1200) };
        timer.Tick += (_, _) => { timer.Stop(); tip.IsOpen = false; };
        timer.Start();
    }
}
