using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

internal sealed class EmergencyHotKeyController : IDisposable
{
    private readonly Action _onTriggered;
    private EmergencyHotKeyService? _service;
    private EmergencyHotKeyGesture _gesture = EmergencyHotKeyGesture.Default;
    private bool _enabled;
    private bool _disposed;

    internal EmergencyHotKeyController(Action onTriggered)
    {
        _onTriggered = onTriggered ?? throw new ArgumentNullException(nameof(onTriggered));
    }

    internal EmergencyHotKeyRegistrationStatus Status { get; private set; } =
        EmergencyHotKeyRegistrationStatus.NotStarted;

    internal EmergencyHotKeyRegistrationStatus Apply(
        bool enabled,
        EmergencyHotKeyGesture gesture)
    {
        if (_disposed)
        {
            return EmergencyHotKeyRegistrationStatus.Stopped;
        }

        if (!enabled)
        {
            StopCurrentService();
            _enabled = false;
            _gesture = gesture;
            Status = EmergencyHotKeyRegistrationStatus.Disabled;
            return Status;
        }

        if (_enabled && _service is not null && _gesture == gesture)
        {
            Status = _service.Status;
            return Status;
        }

        StopCurrentService();
        _gesture = gesture;
        _enabled = true;
        _service = new EmergencyHotKeyService(_onTriggered, gesture);
        _ = _service.Start();
        Status = _service.Status;
        return Status;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopCurrentService();
        Status = EmergencyHotKeyRegistrationStatus.Stopped;
    }

    private void StopCurrentService()
    {
        _service?.Dispose();
        _service = null;
    }
}
