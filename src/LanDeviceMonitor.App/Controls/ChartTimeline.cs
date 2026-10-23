namespace LanDeviceMonitor.Controls;

internal sealed class ChartTimeline
{
    private double _anchorTime;
    private double _anchorTick;
    private double _sourceTime;
    private double _delay;
    private bool _initialized;

    public double Read(double now) => !_initialized ? 0 : _anchorTime + Math.Max(0, now - _anchorTick);

    public void Observe(double sourceTime, double intervalSeconds, double now)
    {
        // One source period supplies both ends of a measured segment. Two seconds
        // cover whole-second timestamps and the view's one-second refresh cadence.
        var delay = Math.Clamp(intervalSeconds, 1, 60) + 2;
        var target = sourceTime - delay;
        var reset = !_initialized || delay != _delay;
        if (reset || sourceTime != _sourceTime)
        {
            _anchorTime = reset ? target : Math.Max(target, Read(now));
            _anchorTick = now;
            _sourceTime = sourceTime;
            _delay = delay;
        }
        _initialized = true;
    }

    public void Reset() => _initialized = false;
}
