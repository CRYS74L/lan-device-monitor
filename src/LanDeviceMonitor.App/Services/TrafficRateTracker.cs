namespace LanDeviceMonitor.Services;

internal sealed class TrafficRateTracker
{
    private bool _hasSample;
    private long? _instantaneousSpeed;
    private long? _total;
    private DateTimeOffset _zeroSince;
    private DateTimeOffset _lastPublishedAt;
    private long _lastDisplaySpeed;

    internal readonly record struct Sample(long DisplaySpeed, long GraphSpeed, bool IsFresh);

    public Sample Observe(DateTimeOffset now, long? instantaneousSpeed, long? total,
        long derivedSpeed, long previousDisplaySpeed, TimeSpan sourceInterval)
    {
        instantaneousSpeed = instantaneousSpeed is { } speed ? Math.Max(0, speed) : null;
        if (_hasSample) previousDisplaySpeed = _lastDisplaySpeed;
        derivedSpeed = Math.Max(0, derivedSpeed);
        var totalAdvanced = derivedSpeed > 0;
        var speedChanged = instantaneousSpeed.HasValue && instantaneousSpeed != _instantaneousSpeed;
        var zeroEvidence = instantaneousSpeed == 0 ||
            !instantaneousSpeed.HasValue && total.HasValue && (!_total.HasValue || total == _total);
        var hasRate = instantaneousSpeed.HasValue || totalAdvanced;
        var idleConfirmed = false;

        // Repeated zeros may be one cached router snapshot. A full source
        // interval without byte growth corroborates idle; renewed traffic clears
        // the candidate immediately. Missing fields never provide zero evidence.
        if (zeroEvidence && !totalAdvanced)
        {
            if (_zeroSince == default) _zeroSince = now;
            idleConfirmed = now - _zeroSince >= sourceInterval;
            hasRate = idleConfirmed || !_hasSample && instantaneousSpeed == 0;
        }
        else if (instantaneousSpeed > 0 || totalAdvanced)
            _zeroSince = default;
        var idleHeartbeat = idleConfirmed && now - _lastPublishedAt >= sourceInterval;
        var fresh = hasRate && (!_hasSample || speedChanged || totalAdvanced || idleHeartbeat);
        if (_hasSample && zeroEvidence && !totalAdvanced && previousDisplaySpeed > 0 && !idleConfirmed)
            fresh = false;
        if (!fresh)
        {
            if (total.HasValue) _total = total;
            return new(_hasSample ? _lastDisplaySpeed : previousDisplaySpeed, 0, false);
        }

        _hasSample = true;
        _lastPublishedAt = now;
        if (instantaneousSpeed.HasValue) _instantaneousSpeed = instantaneousSpeed;
        if (total.HasValue) _total = total;

        // A positive byte delta measures traffic in this interval even when the
        // instantaneous field is absent or temporarily zero. Positive reported
        // rates keep their original meaning; genuine idle snapshots remain zero.
        var value = instantaneousSpeed > 0 ? instantaneousSpeed.Value : derivedSpeed;
        _lastDisplaySpeed = value;
        return new(value, value, true);
    }
}
