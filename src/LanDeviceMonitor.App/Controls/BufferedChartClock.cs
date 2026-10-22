namespace LanDeviceMonitor.Controls;

internal sealed class BufferedChartClock
{
    private double _from;
    private double _target;
    private double _started;
    private double _duration;
    private bool _initialized;

    public double Read(double now) => !_initialized ? 0 : _from + (_target - _from) *
        Math.Clamp((now - _started) / Math.Max(.001, _duration), 0, 1);

    public void Observe(double target, double previous, double now)
    {
        if (_initialized && target == _target) return;
        var current = _initialized && target > _target ? Read(now) : previous;
        _duration = Math.Max(.1, target - (_initialized && target > _target ? _target : previous));
        _from = Math.Min(current, target);
        _target = target;
        _started = now;
        _initialized = true;
    }
    public void Reset() => _initialized = false;
}
