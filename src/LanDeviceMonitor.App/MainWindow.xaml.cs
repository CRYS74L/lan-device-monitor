using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    private readonly NotificationService _notificationService;
    private readonly MonitoringService _monitoringService;
    private readonly ObservableCollection<DeviceItemViewModel> _devices = [];
    private ICollectionView? _deviceView;
    private DeviceItemViewModel? _selectedDevice;
    private bool _isExiting;
    private bool _isConnecting;
    private bool _showingSettings;

    public MainWindow(NotificationService notificationService)
    {
        InitializeComponent();
        DataContext = this;
        _notificationService = notificationService;
        _monitoringService = new MonitoringService(new XiaomiRouterClient(), _repository);
        _monitoringService.Updated += MonitoringService_Updated;
        _monitoringService.ConnectionStateChanged += MonitoringService_ConnectionStateChanged;
        _monitoringService.MonitoringError += MonitoringService_MonitoringError;

        DeviceList.ItemsSource = _devices;
        DeviceTypeCombo.ItemsSource = DeviceIdentityService.AvailableTypes
            .Select(identity => new DeviceTypeOption(identity.TypeName, identity.IconGlyph))
            .ToList();
        AddHandler(
            Mouse.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(PreventSelectorWheelChange),
            handledEventsToo: true);
        Loaded += MainWindow_Loaded;
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

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
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
            ShowDevicesPanel();
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

    private void ReplaceDevices(IReadOnlyList<DeviceViewData> devices, bool preserveSelection)
    {
        var selectedMac = preserveSelection ? SelectedDevice?.MacAddress : null;
        _devices.Clear();
        foreach (var device in devices)
        {
            _devices.Add(new DeviceItemViewModel(device));
        }

        _deviceView = CollectionViewSource.GetDefaultView(_devices);
        _deviceView.Filter = FilterDevice;
        OnlineCountText.Text = _devices.Count(device => device.IsOnline).ToString();
        OfflineCountText.Text = _devices.Count(device => !device.IsOnline).ToString();

        var selected = selectedMac is null
            ? _devices.FirstOrDefault()
            : _devices.FirstOrDefault(device =>
                device.MacAddress.Equals(selectedMac, StringComparison.OrdinalIgnoreCase));
        DeviceList.SelectedItem = selected;
        UpdateSelectedDevice(selected);
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

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectedDevice(DeviceList.SelectedItem as DeviceItemViewModel);

    private void UpdateSelectedDevice(DeviceItemViewModel? device)
    {
        var selectionChanged = !string.Equals(
            SelectedDevice?.MacAddress,
            device?.MacAddress,
            StringComparison.OrdinalIgnoreCase);
        SelectedDevice = device;
        var hasDevice = device is not null;
        if (!_showingSettings)
        {
            EmptyPanel.Visibility = hasDevice ? Visibility.Collapsed : Visibility.Visible;
            DetailPanel.Visibility = hasDevice ? Visibility.Visible : Visibility.Collapsed;
            if (hasDevice && selectionChanged)
            {
                AnimatePanelIn(DetailPanel);
            }
        }

        if (device is null)
        {
            return;
        }

        CustomNameBox.Text = device.Profile.CustomName;
        NotifyOnlineCheck.IsChecked = device.Profile.NotifyWhenOnline;
        NotifyOfflineCheck.IsChecked = device.Profile.NotifyWhenOffline;
        DeviceTypeCombo.SelectedItem = DeviceTypeCombo.Items
            .Cast<DeviceTypeOption>()
            .FirstOrDefault(option => option.TypeName == device.TypeName);

        DetailStatusText.Text = device.StatusText;
        DetailStatusBadge.Background = device.IsOnline
            ? new SolidColorBrush(MediaColor.FromRgb(221, 245, 232))
            : new SolidColorBrush(MediaColor.FromRgb(228, 231, 236));
        DetailStatusText.Foreground = device.IsOnline
            ? new SolidColorBrush(MediaColor.FromRgb(20, 121, 74))
            : new SolidColorBrush(MediaColor.FromRgb(107, 114, 128));
    }

    private async void SaveDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var selectedType = DeviceTypeCombo.SelectedItem as DeviceTypeOption;
        await _repository.UpdateDeviceProfileAsync(
            SelectedDevice.MacAddress,
            CustomNameBox.Text,
            selectedType?.TypeName ?? string.Empty,
            selectedType?.IconGlyph ?? string.Empty,
            NotifyOnlineCheck.IsChecked == true,
            NotifyOfflineCheck.IsChecked == true);
        ReplaceDevices(await _repository.GetDevicesAsync(), preserveSelection: true);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        PopulateSettings(_repository.Settings);
        SettingsMessageBorder.Visibility = Visibility.Collapsed;
        ShowSettingsPanel();
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
            await _repository.UpdateSettingsAsync(
                address,
                interval,
                CloseToTrayCheck.IsChecked == true);
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

    private void CancelSettingsButton_Click(object sender, RoutedEventArgs e) => ShowDevicesPanel();

    private void PopulateSettings(AppSettings settings)
    {
        RouterAddressBox.Text = settings.RouterAddress;
        CloseToTrayCheck.IsChecked = settings.CloseToTray;
        RefreshIntervalCombo.SelectedItem = RefreshIntervalCombo.Items
            .Cast<ComboBoxItem>()
            .FirstOrDefault(item => item.Content?.ToString() == settings.RefreshIntervalSeconds.ToString())
            ?? RefreshIntervalCombo.Items.Cast<ComboBoxItem>().First(item => item.Content?.ToString() == "3");
    }

    private void ShowSettings(string message)
    {
        SettingsMessageText.Text = message;
        SettingsMessageBorder.Visibility = Visibility.Visible;
        ShowSettingsPanel();
    }

    private void ShowSettingsPanel()
    {
        _showingSettings = true;
        EmptyPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        AnimatePanelIn(SettingsPanel);
    }

    private void ShowDevicesPanel()
    {
        _showingSettings = false;
        SettingsPanel.Visibility = Visibility.Collapsed;
        UpdateSelectedDevice(DeviceList.SelectedItem as DeviceItemViewModel);
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

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        panel.Opacity = 0;
        var translate = new TranslateTransform(0, 7);
        panel.RenderTransform = translate;
        panel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = easing
            });
        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = easing
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

    public void RequestExit()
    {
        _isExiting = true;
        _monitoringService.Dispose();
        Close();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
