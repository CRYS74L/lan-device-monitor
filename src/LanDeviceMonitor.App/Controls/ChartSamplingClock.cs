using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Controls;

// Sampling slots and wall-clock arrivals have different phases. Apply that same
// phase to the viewport, without moving historical slots or changing the buffer.
internal sealed class ChartSamplingClock
{
    private double _target, _offset, _tick;
    private bool _initialized;

    internal void Observe(IReadOnlyList<TrafficSample> samples)
    {
        var offsets = samples.TakeLast(8).Where(s => s.ReceivedAt.HasValue)
            .Select(s => (s.Timestamp - s.ReceivedAt!.Value).TotalSeconds).Order().ToArray();
        if (offsets.Length > 0)
            _target = (offsets[(offsets.Length - 1) / 2] + offsets[offsets.Length / 2]) / 2;
    }

    internal double Read(double tick)
    {
        var elapsed = tick - _tick;
        // Resume at the current phase, without playing through hidden history.
        if (!_initialized || elapsed > 1) _offset = _target;
        else _offset += Math.Clamp(_target - _offset, -Math.Max(0, elapsed) * .1, Math.Max(0, elapsed) * .1);
        _tick = tick; _initialized = true;
        return _offset;
    }

    internal void Reset() { _target = _offset = _tick = 0; _initialized = false; }
}
