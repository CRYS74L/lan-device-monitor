using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace LanDeviceMonitor;

public partial class MainWindow
{
    private double _expandedSidebarWidth = 390;
    private bool _sidebarCollapsed;
    internal Action<string> CopyNetworkText { get; set; } = System.Windows.Clipboard.SetText;

    private void WorkspaceGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSidebarBounds();

    private void UpdateSidebarBounds()
    {
        if (SidebarColumn is null || _sidebarCollapsed || WorkspaceGrid.ActualWidth <= 0) return;
        SidebarColumn.MaxWidth = Math.Max(280, Math.Min(520, WorkspaceGrid.ActualWidth - 606));
        if (SidebarColumn.Width.Value > SidebarColumn.MaxWidth)
            SidebarColumn.Width = new GridLength(SidebarColumn.MaxWidth);
    }

    private void SidebarSplitter_DragCompleted(object sender, DragCompletedEventArgs e) =>
        _expandedSidebarWidth = SidebarColumn.ActualWidth;

    private void SidebarToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_sidebarCollapsed) _expandedSidebarWidth = SidebarColumn.ActualWidth;
        _sidebarCollapsed = !_sidebarCollapsed;
        DeviceSidebar.Visibility = SidebarSplitter.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarColumn.MinWidth = _sidebarCollapsed ? 0 : 280;
        SidebarColumn.MaxWidth = _sidebarCollapsed ? 0 : 520;
        SidebarColumn.Width = new GridLength(_sidebarCollapsed ? 0 : _expandedSidebarWidth);
        SidebarDividerColumn.Width = new GridLength(_sidebarCollapsed ? 0 : 6);
        DetailSurface.Margin = new Thickness(_sidebarCollapsed ? 10 : 2, 10, 10, 10);
        SidebarToggleButton.Content = _sidebarCollapsed ? "展开侧栏" : "收起侧栏";
        System.Windows.Automation.AutomationProperties.SetName(SidebarToggleButton, _sidebarCollapsed ? "展开设备侧栏" : "收起设备侧栏");
        UpdateSidebarBounds();
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
