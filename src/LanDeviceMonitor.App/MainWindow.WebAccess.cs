using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services.WebAccess;
using QRCoder;

namespace LanDeviceMonitor;

public partial class MainWindow
{
    private ProtectedWebStore? _webStore;
    private WebPushService? _webPush;
    private LanWebServer? _lanWebServer;
    private CancellationTokenSource? _webMeshCancellation;
    private bool _applyingWebAccess;

    private async Task InitializeWebAccessAsync()
    {
        if (!_repository.Settings.LanAccessEnabled) return;
        try { await StartWebAccessAsync(); }
        catch (Exception e) { WebAccessStatus.Text = "局域网服务未启动：" + e.Message; }
    }

    private async Task StartWebAccessAsync()
    {
        _webStore ??= new ProtectedWebStore();
        _webPush ??= new WebPushService(_webStore);
        _lanWebServer ??= new LanWebServer(_webStore, _webPush);
        _webMeshCancellation?.Cancel();
        _webMeshCancellation?.Dispose();
        _webMeshCancellation = new CancellationTokenSource();
        var settings = _repository.Settings;
        await _lanWebServer.StartAsync(settings.LanHttpsPort, settings.LanSetupPort);
        _webPush.Enabled = settings.WebPushEnabled;
        _lanWebServer.Publish(new MonitorUpdate(await _repository.GetDevicesAsync(), [], DateTimeOffset.MinValue));
        UpdateWebAccessDisplay();
        _ = RefreshWebMeshAsync(_webMeshCancellation.Token);
    }

    private async Task RefreshWebMeshAsync(CancellationToken ct)
    {
        try
        {
            // One shared request per minute, regardless of browser/device count.
            while (!ct.IsCancellationRequested)
            {
                try { _lanWebServer?.SetMesh(await _monitoringService.GetMeshNodesAsync(ct)); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch { /* monitoring connection errors are reported by the main loop */ }
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void UpdateWebAccessDisplay()
    {
        if (_lanWebServer?.IsRunning != true || _webStore is null)
        {
            WebAccessStatus.Text = "局域网访问已关闭，网页和推送服务均已停止。";
            LanAddressBox.Text = ""; LanAccessCodeBox.Text = ""; LanQrImage.Source = null;
            LanSetupAddressBox.Text = "";
            return;
        }
        var urls = _lanWebServer.Urls.Where(u => u.StartsWith("https://") && !u.Contains("127.0.0.1")).ToArray();
        var first = urls.FirstOrDefault() ?? _lanWebServer.Urls.First(u => u.StartsWith("https://"));
        LanAddressBox.Text = string.Join(Environment.NewLine, urls.Length > 0 ? urls : [first]);
        LanAccessCodeBox.Text = _webStore.AccessToken;
        var uri = new Uri(first);
        LanSetupAddressBox.Text = $"http://{uri.Host}:{_repository.Settings.LanSetupPort}/";
        WebCertificateText.Text = "CA 证书 SHA-256：" + _webStore.Fingerprint;
        WebAccessStatus.Text = $"服务已启动 · 已订阅 {_webStore.Subscriptions.Count} 台浏览器 · {(_webPush!.Enabled ? "Web Push 已开启" : "Web Push 已关闭")}。关闭窗口到托盘后仍继续运行。";
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(first + "/#access=" + _webStore.AccessToken, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        using var stream = new MemoryStream(qr.GetGraphic(5));
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        LanQrImage.Source = bitmap;
    }

    private async void WebAccessToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isPopulatingAppSettings || !IsLoaded || _applyingWebAccess) return;
        await ApplyWebAccessAsync();
    }

    private async void ApplyWebAccess_Click(object sender, RoutedEventArgs e) => await ApplyWebAccessAsync();

    private async void WebPushToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isPopulatingAppSettings || !IsLoaded || _applyingWebAccess) return;
        await ApplyWebPushToggleAsync();
    }

    private async Task ApplyWebPushToggleAsync()
    {
        var enabled = WebPushCheck.IsChecked == true;
        if (_webPush is not null) _webPush.Enabled = enabled && _lanWebServer?.IsRunning == true;
        try
        {
            var s = _repository.Settings;
            await _repository.UpdateWebSettingsAsync(s.LanAccessEnabled, enabled, s.LanHttpsPort, s.LanSetupPort);
            UpdateWebAccessDisplay();
        }
        catch (Exception ex) { WebAccessStatus.Text = "通知开关保存失败：" + ex.Message; }
    }
    private async Task ApplyWebAccessAsync()
    {
        if (_applyingWebAccess) return;
        _applyingWebAccess = true;
        LanAccessCheck.IsEnabled = false; WebPushCheck.IsEnabled = false;
        try
        {
            // Turning off must work even when a port editor contains invalid text.
            if (LanAccessCheck.IsChecked != true)
            {
                _webMeshCancellation?.Cancel();
                if (_lanWebServer is not null) await _lanWebServer.StopAsync();
                var current = _repository.Settings;
                await _repository.UpdateWebSettingsAsync(false, WebPushCheck.IsChecked == true, current.LanHttpsPort, current.LanSetupPort);
                UpdateWebAccessDisplay();
                return;
            }
            if (!int.TryParse(LanHttpsPortBox.Text, out var https) || !int.TryParse(LanSetupPortBox.Text, out var setup))
                throw new ArgumentException("端口必须是数字。");
            await _repository.UpdateWebSettingsAsync(LanAccessCheck.IsChecked == true, WebPushCheck.IsChecked == true, https, setup);
            if (LanAccessCheck.IsChecked == true) await StartWebAccessAsync();
            else
            {
                _webMeshCancellation?.Cancel();
                if (_lanWebServer is not null) await _lanWebServer.StopAsync();
                UpdateWebAccessDisplay();
            }
        }
        catch (Exception e) { UpdateWebAccessDisplay(); WebAccessStatus.Text = $"应用失败 · {(_lanWebServer?.IsRunning == true ? "原服务仍运行" : "服务未运行")}：{e.Message}"; }
        finally { _applyingWebAccess = false; LanAccessCheck.IsEnabled = true; WebPushCheck.IsEnabled = true; }
    }

    private void CopyLanAddress_Click(object sender, RoutedEventArgs e)
    {
        if (_lanWebServer?.IsRunning != true || _webStore is null) return;
        var url = LanAddressBox.Text.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries).First();
        System.Windows.Clipboard.SetText(url + "/#access=" + _webStore.AccessToken);
        WebAccessStatus.Text = "已复制含访问授权的链接，请只分享给可信设备。";
    }

    private void ExportWebCertificate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _webStore ??= new ProtectedWebStore();
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "LAN-Device-Assistant-CA.cer", Filter = "CA 证书 (*.cer)|*.cer" };
            if (dialog.ShowDialog(this) == true) File.WriteAllBytes(dialog.FileName, _webStore.RootCertificate);
        }
        catch (Exception ex) { WebAccessStatus.Text = "证书导出失败：" + ex.Message; }
    }

    private void ResetWebAccess_Click(object sender, RoutedEventArgs e)
    {
        if (_webStore is null) return;
        if (System.Windows.MessageBox.Show(this, "重置后，所有已连接网页必须重新输入访问码，并重新启用通知。继续吗？", "重置网页授权", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        try { _webPush!.Enabled = false; _webStore.ResetAccess(); _webPush.Enabled = _lanWebServer?.IsRunning == true && _repository.Settings.WebPushEnabled; UpdateWebAccessDisplay(); }
        catch (Exception ex) { WebAccessStatus.Text = "重置失败：" + ex.Message; }
    }
}
