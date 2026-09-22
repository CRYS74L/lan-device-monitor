using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using WebPush;

namespace LanDeviceMonitor.Services.WebAccess;

internal sealed record WebSubscription(string Endpoint, string P256dh, string Auth);

internal sealed class ProtectedWebStore
{
    private sealed class State
    {
        public string AccessToken { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        public string PublicKey { get; set; } = "";
        public string PrivateKey { get; set; } = "";
        public string RootPfx { get; set; } = "";
        public List<WebSubscription> Subscriptions { get; set; } = [];
    }
    private readonly object _gate = new();
    private readonly string _path;
    private State _state;
    public ProtectedWebStore(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanDeviceMonitor");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "web-access.dat");
        if (File.Exists(_path))
            _state = JsonSerializer.Deserialize<State>(ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser))
                ?? throw new InvalidDataException("网页访问配置损坏；请恢复配置备份。");
        else
        {
            var keys = VapidHelper.GenerateVapidKeys();
            _state = new State { PublicKey = keys.PublicKey, PrivateKey = keys.PrivateKey };
            using var key = RSA.Create(3072);
            var req = new CertificateRequest("CN=LAN Device Assistant Local CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
            using var root = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
            _state.RootPfx = Convert.ToBase64String(root.Export(X509ContentType.Pfx));
            Save();
        }
    }
    public string AccessToken { get { lock (_gate) return _state.AccessToken; } }
    public string PublicKey { get { lock (_gate) return _state.PublicKey; } }
    public VapidDetails Vapid => new("https://github.com/CRYS74L/lan-device-monitor", _state.PublicKey, _state.PrivateKey);
    public IReadOnlyList<WebSubscription> Subscriptions { get { lock (_gate) return _state.Subscriptions.ToArray(); } }
    public bool Authenticate(string token) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(AccessToken));
    public void ResetAccess() { lock (_gate) { _state.AccessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)); _state.Subscriptions.Clear(); Save(); } }
    public void Add(WebSubscription s, string accessToken)
    {
        lock (_gate)
        {
            if (!Authenticate(accessToken)) throw new UnauthorizedAccessException("授权已被重置，请重新连接。");
            if (_state.Subscriptions.Any(x => x.Endpoint == s.Endpoint)) _state.Subscriptions.RemoveAll(x => x.Endpoint == s.Endpoint);
            else if (_state.Subscriptions.Count >= 32) throw new InvalidOperationException("最多允许 32 个浏览器订阅，请重置旧授权。");
            _state.Subscriptions.Add(s); Save();
        }
    }
    public bool Contains(string endpoint) { lock (_gate) return _state.Subscriptions.Any(x => x.Endpoint == endpoint); }
    public void Remove(string endpoint) { lock (_gate) { _state.Subscriptions.RemoveAll(x => x.Endpoint == endpoint); Save(); } }
    private X509Certificate2 Root() => new(Convert.FromBase64String(_state.RootPfx), (string?)null, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    public byte[] RootCertificate { get { using var root = Root(); return root.Export(X509ContentType.Cert); } }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(RootCertificate));
    public X509Certificate2 CreateServerCertificate(IEnumerable<IPAddress> addresses)
    {
        using var root = Root();
        using var key = RSA.Create(2048);
        var req = new CertificateRequest("CN=LAN Device Assistant", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var eku = new OidCollection { new("1.3.6.1.5.5.7.3.1") };
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, true));
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost");
        foreach (var address in addresses.Distinct()) san.AddIpAddress(address);
        req.CertificateExtensions.Add(san.Build());
        using var leaf = req.Create(root, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(90), RandomNumberGenerator.GetBytes(16));
        using var withKey = leaf.CopyWithPrivateKey(key);
        // Windows Schannel cannot use an ephemeral private key for TLS. The default
        // user key container is removed on certificate disposal; do not PersistKeySet.
        return new X509Certificate2(withKey.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.UserKeySet);
    }
    private void Save()
    {
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(_state), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path + ".tmp", bytes); File.Move(_path + ".tmp", _path, true);
    }
}
