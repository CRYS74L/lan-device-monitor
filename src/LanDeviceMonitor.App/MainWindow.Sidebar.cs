using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace LanDeviceMonitor;

public partial class MainWindow
{
    internal const double CollapsedSidebarWidth = 56;
    private double _expandedSidebarWidth = 390;
    private System.Windows.Threading.DispatcherTimer? _sidebarHoverTimer;
    private bool _collapsedSidebarHovered;
    private bool _collapsedSidebarFocused;
    private int _sidebarHintGeneration;
    private bool _sidebarCollapsed;
    private bool _sidebarAnimating;
    private int _sidebarAnimationGeneration;
    internal Action<string> CopyNetworkText { get; set; } = System.Windows.Clipboard.SetText;
    private static readonly DependencyProperty SidebarAnimationWidthProperty = DependencyProperty.Register(
        "SidebarAnimationWidth", typeof(double), typeof(MainWindow), new PropertyMetadata(390d, (owner, args) =>
        {
            var window = (MainWindow)owner;
            if (window.SidebarColumn is null) return;
            var width = Math.Max(CollapsedSidebarWidth, (double)args.NewValue);
            window.SidebarColumn.Width = new GridLength(width);
            window.SidebarDividerColumn.Width = new GridLength(1 + Math.Clamp((width - CollapsedSidebarWidth) * 5 / (280 - CollapsedSidebarWidth), 0, 5));
        }));

    private double MaximumSidebarWidth => Math.Max(280, Math.Min(520, WorkspaceGrid.ActualWidth - 606));

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
            SidebarExpandedContent.Width = Math.Max(0, DeviceSidebar.ActualWidth - 22);
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
        var collapsedOpacity = CollapsedSidebarButton.Opacity;
        var targetWidth = _sidebarCollapsed ? CollapsedSidebarWidth : Math.Clamp(_expandedSidebarWidth, 280, MaximumSidebarWidth);
        var generation = ++_sidebarAnimationGeneration;
        BeginAnimation(SidebarAnimationWidthProperty, null);
        SidebarExpandedContent.BeginAnimation(OpacityProperty, null);
        CollapsedSidebarButton.BeginAnimation(OpacityProperty, null);
        _sidebarAnimating = animate && Math.Abs(currentWidth - targetWidth) > 0.1;
        SidebarColumn.MinWidth = CollapsedSidebarWidth;
        SidebarColumn.MaxWidth = MaximumSidebarWidth;
        DeviceSidebar.Visibility = Visibility.Visible;
        SidebarExpandedContent.Visibility = Visibility.Visible;
        CollapsedSidebarButton.Visibility = Visibility.Visible;
        CollapsedSidebarButton.IsHitTestVisible = _sidebarCollapsed;
        CancelCollapsedHint();
        SidebarSplitter.Visibility = Visibility.Visible;
        SidebarSplitter.IsEnabled = !_sidebarAnimating && !_sidebarCollapsed;
        SidebarExpandedContent.IsHitTestVisible = !_sidebarCollapsed;
        SidebarExpandedContent.Width = Math.Max(0, Math.Clamp(_expandedSidebarWidth, 280, MaximumSidebarWidth) - 22);
        SidebarToggleButton.ToolTip = "收起侧栏";
        System.Windows.Automation.AutomationProperties.SetName(SidebarToggleButton, "收起侧栏");

        void Finish()
        {
            if (generation != _sidebarAnimationGeneration) return;
            BeginAnimation(SidebarAnimationWidthProperty, null);
            SetValue(SidebarAnimationWidthProperty, targetWidth);
            SidebarColumn.Width = new GridLength(targetWidth);
            SidebarDividerColumn.Width = new GridLength(_sidebarCollapsed ? 1 : 6);
            SidebarExpandedContent.BeginAnimation(OpacityProperty, null);
            SidebarExpandedContent.Opacity = _sidebarCollapsed ? 0 : 1;
            CollapsedSidebarButton.BeginAnimation(OpacityProperty, null);
            CollapsedSidebarButton.Opacity = _sidebarCollapsed ? 1 : 0;
            _sidebarAnimating = false;
            SidebarExpandedContent.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            CollapsedSidebarButton.Visibility = _sidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;
            SidebarSplitter.Visibility = _sidebarCollapsed ? Visibility.Hidden : Visibility.Visible;
            SidebarColumn.MinWidth = _sidebarCollapsed ? CollapsedSidebarWidth : 280;
            SidebarColumn.MaxWidth = _sidebarCollapsed ? CollapsedSidebarWidth : MaximumSidebarWidth;
            if (_sidebarCollapsed && SidebarExpandedContent.IsKeyboardFocusWithin) CollapsedSidebarButton.Focus();
            if (!_sidebarCollapsed && CollapsedSidebarButton.IsKeyboardFocusWithin) SidebarToggleButton.Focus();
            SidebarSplitter.IsEnabled = !_sidebarCollapsed;
            if (!_sidebarCollapsed) SidebarExpandedContent.Width = Math.Max(0, targetWidth - 22);
        }

        if (!_sidebarAnimating) { Finish(); return; }
        SetValue(SidebarAnimationWidthProperty, currentWidth);
        SidebarExpandedContent.Opacity = currentOpacity;
        CollapsedSidebarButton.Opacity = collapsedOpacity;
        var widthAnimation = new System.Windows.Media.Animation.DoubleAnimation(currentWidth, targetWidth, TimeSpan.FromMilliseconds(260))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        widthAnimation.Completed += (_, _) => Finish();
        BeginAnimation(SidebarAnimationWidthProperty, widthAnimation);
        SidebarExpandedContent.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(currentOpacity, _sidebarCollapsed ? 0 : 1, TimeSpan.FromMilliseconds(160))
            { BeginTime = TimeSpan.FromMilliseconds(_sidebarCollapsed ? 0 : 60) });
        CollapsedSidebarButton.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(collapsedOpacity, _sidebarCollapsed ? 1 : 0, TimeSpan.FromMilliseconds(120))
            { BeginTime = TimeSpan.FromMilliseconds(_sidebarCollapsed ? 100 : 0) });
    }

    private void CollapsedSidebarButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_sidebarCollapsed) return;
        _sidebarCollapsed = false;
        ApplySidebarState(SystemParameters.ClientAreaAnimation && IsLoaded);
    }

    private void CancelCollapsedHint()
    {
        _sidebarHintGeneration++;
        _sidebarHoverTimer?.Stop();
        _collapsedSidebarHovered = false;
        SetCollapsedGlyph(false, animate: false);
    }

    private void SetCollapsedGlyph(bool show, bool animate = true)
    {
        void Transition(FrameworkElement element, double target)
        {
            var current = element.Opacity;
            element.BeginAnimation(OpacityProperty, null);
            if (!animate || !SystemParameters.ClientAreaAnimation || !IsLoaded) { element.Opacity = target; return; }
            element.Opacity = current;
            element.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(current, target, TimeSpan.FromMilliseconds(160))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        }
        Transition(CollapsedAppIcon, show ? 0 : 1);
        Transition(CollapsedSidebarGlyph, show ? 1 : 0);
    }

    private void WindowDragRegion_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
            if (node is System.Windows.Controls.Primitives.ButtonBase) return;
        if (e.ClickCount == 2) { MaximizeWindowButton_Click(sender, e); e.Handled = true; return; }
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) { DragMove(); e.Handled = true; }
    }

    private void CollapsedSidebar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_sidebarCollapsed) return;
        _collapsedSidebarHovered = true;
        _sidebarHoverTimer?.Stop();
        var generation = ++_sidebarHintGeneration;
        if (_collapsedSidebarFocused) { SetCollapsedGlyph(true); return; }
        _sidebarHoverTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input, Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(150) };
        _sidebarHoverTimer.Tick += (_, _) =>
        {
            if (generation != _sidebarHintGeneration) return;
            _sidebarHoverTimer?.Stop();
            if (_sidebarCollapsed && _collapsedSidebarHovered) SetCollapsedGlyph(true);
        };
        _sidebarHoverTimer.Start();
    }

    private void CollapsedSidebar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapsedSidebarHovered = false;
        _sidebarHintGeneration++;
        _sidebarHoverTimer?.Stop();
        SetCollapsedGlyph(_sidebarCollapsed && _collapsedSidebarFocused);
    }

    private void CollapsedSidebar_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        _collapsedSidebarFocused = System.Windows.Input.InputManager.Current.MostRecentInputDevice is System.Windows.Input.KeyboardDevice;
        _sidebarHintGeneration++;
        _sidebarHoverTimer?.Stop();
        if (_sidebarCollapsed && _collapsedSidebarFocused) SetCollapsedGlyph(true);
    }

    private void CollapsedSidebar_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        _collapsedSidebarFocused = false;
        if (!_collapsedSidebarHovered) SetCollapsedGlyph(false);
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
