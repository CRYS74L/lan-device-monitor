using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;
using LanDeviceMonitor.ViewModels;

namespace LanDeviceMonitor;

public sealed partial class MainWindow : Window
{
    private readonly DeviceRepository _repository = new();
    private readonly RouterCredentialService _credentialService = new();
    private readonly MonitoringService _monitoringService;
    private readonly ObservableCollection<DeviceItemViewModel> _devices = [];
    private IReadOnlyList<DeviceViewData> _latestDevices = [];
    private DeviceListFilter _filter = DeviceListFilter.Online;

    public MainWindow()
    {
        InitializeComponent();
        Title = "局域网设备助手";
        SystemBackdrop = new DesktopAcrylicBackdrop();

        _monitoringService = new MonitoringService(
            new XiaomiRouterClient(),
            _repository);
        _monitoringService.Updated += MonitoringService_Updated;
        _monitoringService.ConnectionStateChanged +=
            MonitoringService_ConnectionStateChanged;
        _monitoringService.MonitoringError +=
            MonitoringService_MonitoringError;

        DeviceList.ItemsSource = _devices;
        ConfigureTitleBar();
        _ = InitializeAsync();
    }

    private void ConfigureTitleBar()
    {
        var appWindow = AppWindow;
        appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor =
            Colors.Transparent;
        appWindow.SetIcon("Assets/AppIcon.ico");

        UpdateTitleBarDragRect();
        SizeChanged += (_, _) => UpdateTitleBarDragRect();
        AppWindow.Changed += (_, _) =>
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                var maximized =
                    presenter.State ==
                    OverlappedPresenterState.Maximized;
                MaximizeGlyph.Glyph =
                    maximized ? "\uE923" : "\uE922";
            }
        };
    }

    private void UpdateTitleBarDragRect()
    {
        var scale = Content.XamlRoot?.RasterizationScale ?? 1d;
        var width = AppWindow.Size.Width;
        var height = (int)(32d * scale);
        var right = (int)(46d * 3d * scale) + 8;
        AppWindow.TitleBar.SetDragRectangles(
            new[]
            {
                new Windows.Graphics.RectInt32(
                    0,
                    0,
                    Math.Max(0, width - right),
                    height)
            });
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _repository.LoadAsync();
            var settings = _repository.Settings;
            ThemeService.Apply(
                settings.ThemeMode,
                RootGrid.Resources,
                RootGrid);

            var password = _credentialService.ReadPassword(
                settings.RouterAddress);
            if (string.IsNullOrWhiteSpace(password))
            {
                ConnectionStatusText.Text =
                    "请输入路由器管理员密码";
                return;
            }

            await ConnectAsync(
                settings.RouterAddress,
                password,
                settings.RefreshIntervalSeconds);
        }
        catch (Exception exception)
        {
            ConnectionStatusText.Text = exception.Message;
        }
    }

    private async Task ConnectAsync(
        string address,
        string password,
        int intervalSeconds)
    {
        try
        {
            ConnectionStatusText.Text = "连接中…";
            await _monitoringService.ConnectAndStartAsync(
                address,
                password,
                intervalSeconds);
        }
        catch (Exception exception)
        {
            ConnectionStatusText.Text = exception.Message;
        }
    }

    private void MonitoringService_Updated(
        object? sender,
        MonitorUpdate update)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _latestDevices = update.Devices;
            ReplaceDevices(update.Devices);
            LastRefreshText.Text =
                $"{update.RefreshedAt:HH:mm:ss} 更新";
        });
    }

    private void MonitoringService_ConnectionStateChanged(
        object? sender,
        string state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ConnectionStatusText.Text = state;
            ConnectionDot.Fill = state == "已连接"
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(
                    255, 22, 121, 74))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(
                    255, 156, 163, 175));
        });
    }

    private void MonitoringService_MonitoringError(
        object? sender,
        Exception exception)
    {
        DispatcherQueue.TryEnqueue(() =>
            ConnectionStatusText.Text = exception.Message);
    }

    private void ReplaceDevices(
        IReadOnlyList<DeviceViewData> data)
    {
        var selectedMac =
            (DeviceList.SelectedItem as DeviceItemViewModel)
            ?.MacAddress;
        var filtered = data
            .Where(item =>
                _filter == DeviceListFilter.Online
                    ? item.Profile.LastKnownOnline
                    : !item.Profile.LastKnownOnline)
            .OrderByDescending(item =>
                item.Profile.LastSeenAt)
            .ToList();

        _devices.Clear();
        foreach (var item in filtered)
        {
            var viewModel = new DeviceItemViewModel(item);
            _devices.Add(viewModel);
            if (selectedMac is not null &&
                viewModel.MacAddress == selectedMac)
            {
                DeviceList.SelectedItem = viewModel;
            }
        }

        if (DeviceList.SelectedItem is null &&
            _devices.Count > 0)
        {
            DeviceList.SelectedIndex = 0;
        }

        OnlineCountText.Text = data
            .Count(item => item.Profile.LastKnownOnline)
            .ToString();
        OfflineCountText.Text = data
            .Count(item => !item.Profile.LastKnownOnline)
            .ToString();
    }

    private void DeviceList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (DeviceList.SelectedItem is
            DeviceItemViewModel device)
        {
            DetailTitle.Text = device.DisplayName;
            DetailIpText.Text = $"IP 地址：{device.IpAddress}";
            DetailStatusText.Text =
                device.IsOnline ? "状态：在线" : "状态：离线";
            DetailSpeedText.Text =
                $"下载 {device.DownloadSpeedText} / " +
                $"上传 {device.UploadSpeedText}";
        }
    }

    private void OnlineFilterCard_Tapped(
        object sender,
        TappedRoutedEventArgs e)
    {
        if (_filter != DeviceListFilter.Online)
        {
            _filter = DeviceListFilter.Online;
            ReplaceDevices(_latestDevices);
        }
    }

    private void OfflineFilterCard_Tapped(
        object sender,
        TappedRoutedEventArgs e)
    {
        if (_filter != DeviceListFilter.Offline)
        {
            _filter = DeviceListFilter.Offline;
            ReplaceDevices(_latestDevices);
        }
    }

    private void MinimizeButton_Click(
        object sender,
        RoutedEventArgs e) =>
        (AppWindow.Presenter as OverlappedPresenter)?.Minimize();

    private void MaximizeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            if (presenter.State ==
                OverlappedPresenterState.Maximized)
            {
                presenter.Restore();
            }
            else
            {
                presenter.Maximize();
            }
        }
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e) =>
        Close();

    private void RouterButton_Click(
        object sender,
        RoutedEventArgs e) =>
        DetailTitle.Text = "路由器信息（开发中）";

    private void SettingsButton_Click(
        object sender,
        RoutedEventArgs e) =>
        DetailTitle.Text = "设置（开发中）";

    private enum DeviceListFilter
    {
        Online,
        Offline
    }
}
