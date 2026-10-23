namespace LanDeviceMonitor.Controls;

internal sealed class ChartTimeline
{
    private double _anchorTime;
    private double _anchorTick;
    private double _sourceTime;
    private bool _initialized;

    public double Read(double now) => !_initialized ? 0 : _anchorTime + Math.Max(0, now - _anchorTick);

    public void Observe(double sourceTime, double latestSampleTime, double now)
    {
        // Sample spacing is not a playback duration. New views and resumed
        // rendering start at the current source time, with the newest point visible.
        var current = _initialized ? Read(now) : sourceTime;
        if (!_initialized || sourceTime != _sourceTime || latestSampleTime > current)
        {
            _anchorTime = Math.Max(Math.Max(sourceTime, latestSampleTime), current);
            _anchorTick = now;
            _sourceTime = sourceTime;
        }
        _initialized = true;
    }

    public void Reset() => _initialized = false;
}
