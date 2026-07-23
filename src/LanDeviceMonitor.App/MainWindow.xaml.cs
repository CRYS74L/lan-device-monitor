using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;
using LanDeviceMonitor.ViewModels;
using MediaColor = System.Windows.Media.Color;

namespace LanDeviceMonitor;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DeviceRepository _repository = new();
    private readonly RouterCredentialService _credentialService = new();
    private readonly StartupRegistrationService _startupRegistrationService = new();
    private readonly NotificationService _notificationService;
    private readonly MonitoringService _monitoringService;
    private readonly ObservableCollection<DeviceItemViewModel> _devices = [];
    private ICollectionView? _deviceView;
    private DeviceItemViewModel? _selectedDevice;
    private CancellationTokenSource? _deviceProfileSaveCancellation;
    private CancellationTokenSource? _appSettingsSaveCancellation;
    private PendingDeviceProfile? _pendingDeviceProfile;
    private Task? _initializationTask;
    private readonly Stack<PageKind> _backNavigation = [];
    private readonly Stack<PageKind> _forwardNavigation = [];
    private bool _isExiting;
    private bool _isConnecting;
    private bool _isReplacingDevices;
    private bool _isPopulatingDeviceSettings;
    private bool _isPopulatingAppSettings;
    private bool _showingSettings;
    private bool _showingRouter;
    private bool _isRestartingRouter;

    public MainWindow(NotificationService notificationService)
    {
        InitializeComponent();
        DataContext = this;
        _notificationService = notificationService;
        _monitoringService = new MonitoringService(new XiaomiRouterClient(), _repository);
        _monitoringService.Updated += MonitoringService_Updated;
        _monitoringService.ConnectionStateChanged += MonitoringService_ConnectionStateChanged;
        _monitoringService.MonitoringError += MonitoringService_MonitoringError;
        _monitoringService.RouterRestartProgressChanged +=
            MonitoringService_RouterRestartProgressChanged;

        DeviceList.ItemsSource = _devices;
        _deviceView = CollectionViewSource.GetDefaultView(_devices);
        _deviceView.Filter = FilterDevice;
        DeviceTypeCombo.ItemsSource = DeviceIdentityService.AvailableTypes
            .Select(identity => new DeviceTypeOption(identity.TypeName, identity.IconGlyph))
            .ToList();
        AddHandler(
            Mouse.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(PreventSelectorWheelChange),
            handledEventsToo: true);
        PreviewMouseDown += MainWindow_PreviewMouseDown;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? MinimizeToTrayRequested;

    public DeviceItemViewModel? SelectedDevice
    {
        get => _selectedDevice;
        private set
        {
            if (ReferenceEquals(_selectedDevice, value))
            {
                return;
            }

            _selectedDevice = value;
            OnPropertyChanged();
        }
    }

    private PageKind CurrentPage =>
        _showingSettings
            ? PageKind.Settings
            : _showingRouter
                ? PageKind.Router
                : PageKind.Devices;

    public Task InitializeAsync() =>
        _initializationTask ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        try
        {
            await _repository.LoadAsync();
            var settings = _repository.Settings;
            PopulateSettings(settings);
            ReplaceDevices(await _repository.GetDevicesAsync(), preserveSelection: false);

            var password = _credentialService.ReadPassword(settings.RouterAddress);
            if (string.IsNullOrWhiteSpace(password))
            {
                ShowSettings("首次使用，请输入小米路由器管理员密码。");
                return;
            }

            await ConnectAsync(settings.RouterAddress, password, settings.RefreshIntervalSeconds);
        }
        catch (Exception exception)
        {
            ShowSettings(exception.Message);
        }
    }

    private async Task ConnectAsync(string address, string password, int intervalSeconds)
    {
        if (_isConnecting)
        {
            return;
        }

        _isConnecting = true;
        ConnectButton.IsEnabled = false;
        SettingsMessageBorder.Visibility = Visibility.Collapsed;
        try
        {
            await _monitoringService.ConnectAndStartAsync(address, password, intervalSeconds);
            ShowDevicesPanel(animate: false, recordNavigation: false);
        }
        catch (Exception exception)
        {
            ShowSettings(exception.Message);
        }
        finally
        {
            _isConnecting = false;
            ConnectButton.IsEnabled = true;
        }
    }

    private void MonitoringService_Updated(object? sender, MonitorUpdate update)
    {
        Dispatcher.Invoke(() =>
        {
            // 实时刷新只替换数据，不触发页面进入动画，也不重填正在编辑的控件。
            ReplaceDevices(update.Devices, preserveSelection: true);
            LastRefreshText.Text = $"{update.RefreshedAt:HH:mm:ss} 更新";
            foreach (var transition in update.Transitions.Where(item => item.ShouldNotify))
            {
                _notificationService.ShowDeviceState(
                    transition.DisplayName,
                    transition.IsOnline,
                    transition.OccurredAt);
            }
        });
    }

    private void MonitoringService_ConnectionStateChanged(object? sender, string state)
    {
        Dispatcher.Invoke(() =>
        {
            ConnectionStatusText.Text = state;
            ConnectionDot.Fill = state == "已连接"
                ? new SolidColorBrush(MediaColor.FromRgb(22, 121, 74))
                : new SolidColorBrush(MediaColor.FromRgb(234, 139, 20));
        });
    }

    private void MonitoringService_MonitoringError(object? sender, Exception exception)
    {
        Dispatcher.Invoke(() =>
        {
            ConnectionStatusText.Text = $"连接异常：{exception.Message}";
            ConnectionDot.Fill = new SolidColorBrush(MediaColor.FromRgb(220, 68, 55));
        });
    }

    private void MonitoringService_RouterRestartProgressChanged(object? sender, string state)
    {
        Dispatcher.Invoke(() =>
        {
            RouterRestartStatusText.Text = state;
            ConnectionStatusText.Text = state;
        });
    }

    private void ReplaceDevices(
        IReadOnlyList<DeviceViewData> devices,
        bool preserveSelection,
        bool updateEditor = false)
    {
        var selectedMac = preserveSelection ? SelectedDevice?.MacAddress : null;
        _isReplacingDevices = true;
        try
        {
            for (var index = 0; index < devices.Count; index++)
            {
                var viewModel = new DeviceItemViewModel(devices[index]);
                if (index < _devices.Count)
                {
                    _devices[index] = viewModel;
                }
                else
                {
                    _devices.Add(viewModel);
                }
            }

            while (_devices.Count > devices.Count)
            {
                _devices.RemoveAt(_devices.Count - 1);
            }

            _deviceView?.Refresh();
            OnlineCountText.Text = _devices.Count(device => device.IsOnline).ToString();
            OfflineCountText.Text = _devices.Count(device => !device.IsOnline).ToString();

            var selected = selectedMac is null
                ? _devices.FirstOrDefault()
                : _devices.FirstOrDefault(device =>
                    device.MacAddress.Equals(selectedMac, StringComparison.OrdinalIgnoreCase));
            DeviceList.SelectedItem = selected;
            UpdateSelectedDevice(
                selected,
                updateEditor || !preserveSelection || selectedMac is null);
        }
        finally
        {
            _isReplacingDevices = false;
        }
    }

    private bool FilterDevice(object item)
    {
        if (item is not DeviceItemViewModel device)
        {
            return false;
        }

        var search = SearchBox.Text.Trim();
        return string.IsNullOrWhiteSpace(search) ||
               device.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               device.TypeName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               device.IpAddress.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               device.MacAddress.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => _deviceView?.Refresh();

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isReplacingDevices)
        {
            return;
        }

        var device = DeviceList.SelectedItem as DeviceItemViewModel;
        var selectionChanged = !string.Equals(
            SelectedDevice?.MacAddress,
            device?.MacAddress,
            StringComparison.OrdinalIgnoreCase);
        _deviceProfileSaveCancellation?.Cancel();
        if (CurrentPage != PageKind.Devices)
        {
            ShowDevicesPanel(animate: false);
        }

        UpdateSelectedDevice(device, updateEditor: true);
        if (selectionChanged && device is not null)
        {
            AnimateDeviceChange();
        }
    }

    private void DeviceList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            FindVisualParent<ListBoxItem>(source)?.DataContext is not DeviceItemViewModel device ||
            CurrentPage == PageKind.Devices)
        {
            return;
        }

        var isCurrentDevice = string.Equals(
            SelectedDevice?.MacAddress,
            device.MacAddress,
            StringComparison.OrdinalIgnoreCase);
        ShowDevicesPanel(animate: isCurrentDevice);
    }

    private void UpdateSelectedDevice(DeviceItemViewModel? device, bool updateEditor)
    {
        SelectedDevice = device;
        var hasDevice = device is not null;
        if (!_showingSettings && !_showingRouter)
        {
            EmptyPanel.Visibility = hasDevice ? Visibility.Collapsed : Visibility.Visible;
            DetailPanel.Visibility = hasDevice ? Visibility.Visible : Visibility.Collapsed;
        }

        if (device is null)
        {
            return;
        }

        if (updateEditor)
        {
            _isPopulatingDeviceSettings = true;
            try
            {
                CustomNameBox.Text = device.Profile.CustomName;
                NotifyOnlineCheck.IsChecked = device.Profile.NotifyWhenOnline;
                NotifyOfflineCheck.IsChecked = device.Profile.NotifyWhenOffline;
                DeviceTypeCombo.SelectedItem = DeviceTypeCombo.Items
                    .Cast<DeviceTypeOption>()
                    .FirstOrDefault(option => option.TypeName == device.TypeName);
                AutoSaveStatusText.Text = "修改会自动保存";
            }
            finally
            {
                _isPopulatingDeviceSettings = false;
            }
        }

        DetailStatusText.Text = device.StatusText;
        DetailStatusBadge.Background = device.IsOnline
            ? new SolidColorBrush(MediaColor.FromRgb(221, 245, 232))
            : new SolidColorBrush(MediaColor.FromRgb(228, 231, 236));
        DetailStatusText.Foreground = device.IsOnline
            ? new SolidColorBrush(MediaColor.FromRgb(20, 121, 74))
            : new SolidColorBrush(MediaColor.FromRgb(107, 114, 128));
    }

    private void DeviceNameBox_TextChanged(object sender, TextChangedEventArgs e) =>
        QueueDeviceProfileSave(TimeSpan.FromMilliseconds(180));

    private void DeviceProfileControl_Changed(object sender, RoutedEventArgs e) =>
        QueueDeviceProfileSave(TimeSpan.FromMilliseconds(30));

    private void QueueDeviceProfileSave(TimeSpan delay)
    {
        if (_isPopulatingDeviceSettings ||
            _isReplacingDevices ||
            SelectedDevice is null)
        {
            return;
        }

        var selectedType = DeviceTypeCombo.SelectedItem as DeviceTypeOption;
        _pendingDeviceProfile = new PendingDeviceProfile(
            SelectedDevice.MacAddress,
            CustomNameBox.Text,
            selectedType?.TypeName ?? string.Empty,
            selectedType?.IconGlyph ?? string.Empty,
            NotifyOnlineCheck.IsChecked == true,
            NotifyOfflineCheck.IsChecked == true);

        _deviceProfileSaveCancellation?.Cancel();
        _deviceProfileSaveCancellation?.Dispose();
        _deviceProfileSaveCancellation = new CancellationTokenSource();
        AutoSaveStatusText.Text = "正在自动保存…";
        _ = SavePendingDeviceProfileAsync(delay, _deviceProfileSaveCancellation.Token);
    }

    private async Task SavePendingDeviceProfileAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            var pending = _pendingDeviceProfile;
            if (pending is null)
            {
                return;
            }

            await _repository.UpdateDeviceProfileAsync(
                pending.MacAddress,
                pending.CustomName,
                pending.CustomType,
                pending.CustomIconGlyph,
                pending.NotifyWhenOnline,
                pending.NotifyWhenOffline,
                cancellationToken);

            if (SelectedDevice?.MacAddress.Equals(
                    pending.MacAddress,
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                ReplaceDevices(
                    await _repository.GetDevicesAsync(cancellationToken),
                    preserveSelection: true,
                    updateEditor: false);
                if (ReferenceEquals(_pendingDeviceProfile, pending))
                {
                    AutoSaveStatusText.Text = "已自动保存";
                }
            }

            if (ReferenceEquals(_pendingDeviceProfile, pending))
            {
                _pendingDeviceProfile = null;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AutoSaveStatusText.Text = $"保存失败：{exception.Message}";
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        PopulateSettings(_repository.Settings);
        SettingsMessageBorder.Visibility = Visibility.Collapsed;
        ShowSettingsPanel();
    }

    private async void RouterButton_Click(object sender, RoutedEventArgs e)
    {
        ShowRouterPanel();
        await LoadRouterInfoAsync();
    }

    private async void RefreshRouterInfoButton_Click(object sender, RoutedEventArgs e) =>
        await LoadRouterInfoAsync();

    private async Task LoadRouterInfoAsync()
    {
        RouterInfoLoadingText.Text = "正在读取上网信息与 Mesh 节点…";
        RouterInfoLoadingBorder.Visibility = Visibility.Visible;
        RefreshRouterInfoButton.IsEnabled = false;
        try
        {
            var internetInfoTask = _monitoringService.GetInternetInfoAsync();
            var meshNodesTask = _monitoringService.GetMeshNodesAsync();
            await Task.WhenAll(internetInfoTask, meshNodesTask);
            var info = await internetInfoTask;
            var meshNodes = await meshNodesTask;
            RouterConnectionBadgeText.Text = info.IsConnected ? "互联网已连接" : "互联网未连接";
            RouterConnectionBadge.Background = info.IsConnected
                ? new SolidColorBrush(MediaColor.FromRgb(221, 245, 232))
                : new SolidColorBrush(MediaColor.FromRgb(255, 237, 213));
            Ipv4TypeText.Text = info.Ipv4ConnectionType;
            Ipv4AddressText.Text = info.Ipv4Address;
            Ipv4MaskText.Text = info.Ipv4SubnetMask;
            Ipv4GatewayText.Text = info.Ipv4Gateway;
            Ipv4DnsText.Text = info.Ipv4Dns;
            Ipv6TypeText.Text = info.Ipv6ConnectionType;
            WanIpv6AddressText.Text = info.WanIpv6Address;
            WanIpv6GatewayText.Text = info.WanIpv6Gateway;
            LanIpv6AddressText.Text = info.LanIpv6Address;
            LanIpv6PrefixText.Text = info.LanIpv6Prefix;
            Ipv6DnsText.Text = info.Ipv6Dns;
            MeshNodesItemsControl.ItemsSource = meshNodes;
            MeshNodesEmptyText.Visibility = meshNodes.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            RouterInfoLoadingBorder.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            RouterInfoLoadingText.Text = $"读取失败：{exception.Message}";
        }
        finally
        {
            RefreshRouterInfoButton.IsEnabled = true;
        }
    }

    private void RestartRouterButton_Click(object sender, RoutedEventArgs e) =>
        RestartConfirmBorder.Visibility = Visibility.Visible;

    private void CancelRestartButton_Click(object sender, RoutedEventArgs e) =>
        RestartConfirmBorder.Visibility = Visibility.Collapsed;

    private async void ConfirmRestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRestartingRouter)
        {
            return;
        }

        _isRestartingRouter = true;
        RestartConfirmBorder.Visibility = Visibility.Collapsed;
        RouterRestartProgressBorder.Visibility = Visibility.Visible;
        RouterRestartProgressBar.Visibility = Visibility.Visible;
        RouterRestartProgressBorder.Background = new SolidColorBrush(
            MediaColor.FromArgb(220, 235, 241, 255));
        RouterRestartStatusText.Text = "正在向路由器发送重启指令…";
        RestartRouterButton.IsEnabled = false;
        try
        {
            await _monitoringService.RestartRouterAsync();
            RouterRestartProgressBar.Visibility = Visibility.Collapsed;
            RouterRestartProgressBorder.Background = new SolidColorBrush(
                MediaColor.FromArgb(220, 221, 245, 232));
            RouterRestartStatusText.Text = "重启完成，路由器与互联网连接已恢复";
            AnimateFeedback(RouterRestartProgressBorder);
            await LoadRouterInfoAsync();
        }
        catch (Exception exception)
        {
            RouterRestartProgressBar.Visibility = Visibility.Collapsed;
            RouterRestartProgressBorder.Background = new SolidColorBrush(
                MediaColor.FromArgb(230, 254, 226, 226));
            RouterRestartStatusText.Text = $"重启未完成：{exception.Message}";
        }
        finally
        {
            _isRestartingRouter = false;
            RestartRouterButton.IsEnabled = true;
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var address = RouterAddressBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            ShowSettings("请输入路由器地址。");
            return;
        }

        var password = RouterPasswordBox.Password;
        if (string.IsNullOrEmpty(password))
        {
            password = _credentialService.ReadPassword(address) ?? string.Empty;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowSettings("请输入路由器管理员密码。");
            return;
        }

        var interval = 3;
        if (RefreshIntervalCombo.SelectedItem is ComboBoxItem item)
        {
            int.TryParse(item.Content?.ToString(), out interval);
        }

        try
        {
            await _repository.UpdateRouterAddressAsync(address);
            if (!string.IsNullOrEmpty(RouterPasswordBox.Password))
            {
                _credentialService.SavePassword(address, RouterPasswordBox.Password);
                RouterPasswordBox.Clear();
            }

            await ConnectAsync(address, password, interval);
        }
        catch (Exception exception)
        {
            ShowSettings(exception.Message);
        }
    }

    private void CancelSettingsButton_Click(object sender, RoutedEventArgs e) =>
        ShowDevicesPanel(animate: true);

    private void BackFromRouterButton_Click(object sender, RoutedEventArgs e) =>
        ShowDevicesPanel(animate: true);

    private void PopulateSettings(AppSettings settings)
    {
        _isPopulatingAppSettings = true;
        try
        {
            RouterAddressBox.Text = settings.RouterAddress;
            CloseToTrayCheck.IsChecked = settings.CloseToTray;
            try
            {
                var startupEnabled = _startupRegistrationService.IsEnabled();
                if (startupEnabled)
                {
                    // 将旧版本的普通启动项迁移为托盘静默启动命令。
                    _startupRegistrationService.SetEnabled(true);
                }

                StartWithWindowsCheck.IsChecked = startupEnabled;
            }
            catch (SecurityException)
            {
                StartWithWindowsCheck.IsChecked = settings.StartWithWindows;
            }

            RefreshIntervalCombo.SelectedItem = RefreshIntervalCombo.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(item =>
                    item.Content?.ToString() == settings.RefreshIntervalSeconds.ToString())
                ?? RefreshIntervalCombo.Items
                    .Cast<ComboBoxItem>()
                    .First(item => item.Content?.ToString() == "3");
            AppSettingsAutoSaveText.Text = "修改会自动保存";
        }
        finally
        {
            _isPopulatingAppSettings = false;
        }
    }

    private void AppBehaviorSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isPopulatingAppSettings)
        {
            return;
        }

        _appSettingsSaveCancellation?.Cancel();
        _appSettingsSaveCancellation?.Dispose();
        _appSettingsSaveCancellation = new CancellationTokenSource();
        AppSettingsAutoSaveText.Text = "正在自动保存…";
        _ = SaveBehaviorSettingsAsync(_appSettingsSaveCancellation.Token);
    }

    private async Task SaveBehaviorSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);
            var interval = GetSelectedRefreshInterval();
            var closeToTray = CloseToTrayCheck.IsChecked == true;
            var startWithWindows = StartWithWindowsCheck.IsChecked == true;

            _startupRegistrationService.SetEnabled(startWithWindows);
            await _repository.UpdateBehaviorSettingsAsync(
                interval,
                closeToTray,
                startWithWindows);
            _monitoringService.UpdateRefreshInterval(interval);
            AppSettingsAutoSaveText.Text = "已自动保存";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppSettingsAutoSaveText.Text = "自动保存失败";
            ShowSettings($"保存应用设置失败：{exception.Message}");
        }
    }

    private int GetSelectedRefreshInterval()
    {
        var interval = 3;
        if (RefreshIntervalCombo.SelectedItem is ComboBoxItem item)
        {
            int.TryParse(item.Content?.ToString(), out interval);
        }

        return Math.Clamp(interval, 2, 60);
    }

    private void ShowSettings(string message)
    {
        SettingsMessageText.Text = message;
        SettingsMessageBorder.Visibility = Visibility.Visible;
        ShowSettingsPanel();
    }

    private void ShowSettingsPanel()
    {
        NavigateToPage(PageKind.Settings, animate: true);
    }

    private void ShowRouterPanel()
    {
        NavigateToPage(PageKind.Router, animate: true);
    }

    private void ShowDevicesPanel(bool animate, bool recordNavigation = true)
    {
        NavigateToPage(PageKind.Devices, animate, recordNavigation);
    }

    private void NavigateToPage(
        PageKind target,
        bool animate,
        bool recordNavigation = true)
    {
        var current = CurrentPage;
        if (current != target && recordNavigation)
        {
            _backNavigation.Push(current);
            _forwardNavigation.Clear();
        }

        _showingSettings = target == PageKind.Settings;
        _showingRouter = target == PageKind.Router;
        SettingsPanel.Visibility = Visibility.Collapsed;
        RouterPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Collapsed;

        FrameworkElement? panel = null;
        switch (target)
        {
            case PageKind.Settings:
                SettingsPanel.Visibility = Visibility.Visible;
                panel = SettingsPanel;
                break;
            case PageKind.Router:
                RouterPanel.Visibility = Visibility.Visible;
                panel = RouterPanel;
                break;
            default:
                UpdateSelectedDevice(
                    DeviceList.SelectedItem as DeviceItemViewModel,
                    updateEditor: false);
                panel = DetailPanel.Visibility == Visibility.Visible
                    ? DetailPanel
                    : EmptyPanel;
                break;
        }

        if (animate && panel is not null)
        {
            AnimatePanelIn(panel);
        }
    }

    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1)
        {
            NavigateBack();
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            NavigateForward();
            e.Handled = true;
        }
    }

    private void NavigateBack()
    {
        if (_backNavigation.Count == 0)
        {
            return;
        }

        _forwardNavigation.Push(CurrentPage);
        NavigateToPage(_backNavigation.Pop(), animate: true, recordNavigation: false);
    }

    private void NavigateForward()
    {
        if (_forwardNavigation.Count == 0)
        {
            return;
        }

        _backNavigation.Push(CurrentPage);
        NavigateToPage(_forwardNavigation.Pop(), animate: true, recordNavigation: false);
    }

    private static void PreventSelectorWheelChange(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            FindVisualParent<System.Windows.Controls.ComboBox>(source) is null)
        {
            return;
        }

        e.Handled = true;
    }

    private static T? FindVisualParent<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private static void AnimatePanelIn(FrameworkElement panel)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            panel.Opacity = 1;
            panel.RenderTransform = Transform.Identity;
            return;
        }

        var easing = new QuinticEase { EasingMode = EasingMode.EaseOut };
        panel.Opacity = 0;
        var translate = new TranslateTransform(0, 7);
        panel.RenderTransform = translate;
        panel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = easing
            });
        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(245))
            {
                EasingFunction = easing
            });
    }

    private void AnimateDeviceChange()
    {
        if (!SystemParameters.ClientAreaAnimation ||
            DetailPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        var easing = new QuinticEase { EasingMode = EasingMode.EaseOut };
        DetailPanel.Opacity = 0.28;
        var translate = new TranslateTransform(11, 0);
        DetailPanel.RenderTransform = translate;
        DetailPanel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.28, 1, TimeSpan.FromMilliseconds(235))
            {
                EasingFunction = easing
            });
        translate.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(11, 0, TimeSpan.FromMilliseconds(265))
            {
                EasingFunction = easing
            });
    }

    private static void AnimateFeedback(FrameworkElement element)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        element.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        if (_repository.Settings.CloseToTray)
        {
            e.Cancel = true;
            MinimizeToTrayRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task PrepareForExitAsync()
    {
        _isExiting = true;
        Hide();

        _deviceProfileSaveCancellation?.Cancel();
        _deviceProfileSaveCancellation?.Dispose();
        _deviceProfileSaveCancellation = null;
        _appSettingsSaveCancellation?.Cancel();
        _appSettingsSaveCancellation?.Dispose();
        _appSettingsSaveCancellation = null;
        if (_pendingDeviceProfile is { } pending)
        {
            try
            {
                await _repository.UpdateDeviceProfileAsync(
                    pending.MacAddress,
                    pending.CustomName,
                    pending.CustomType,
                    pending.CustomIconGlyph,
                    pending.NotifyWhenOnline,
                    pending.NotifyWhenOffline);
            }
            catch
            {
                // 退出阶段不再弹出界面；已完成的保存仍然保留。
            }
        }

        _monitoringService.Dispose();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record PendingDeviceProfile(
        string MacAddress,
        string CustomName,
        string CustomType,
        string CustomIconGlyph,
        bool NotifyWhenOnline,
        bool NotifyWhenOffline);

    private enum PageKind
    {
        Devices,
        Router,
        Settings
    }
}
