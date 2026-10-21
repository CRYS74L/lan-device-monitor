namespace LanDeviceMonitor.Services;

internal sealed class TrafficCounterTracker
{
    private long? _total;
    private DateTimeOffset _at;
    private long? _resetTotal;
    private DateTimeOffset _resetAt;

    public long Observe(long? total, DateTimeOffset now)
    {
        if (total is null or < 0) return 0;
        if (!_total.HasValue || now <= _at)
        {
            _total = total;
            _at = now;
            _resetTotal = null;
            return 0;
        }

        if (total < _total)
        {
            // One lower response can be an older snapshot. Confirm a new
            // counter epoch only when the lower counter starts increasing.
            if (_resetTotal.HasValue && total > _resetTotal && now > _resetAt)
            {
                var rate = Rate(total.Value - _resetTotal.Value, now - _resetAt);
                _total = total;
                _at = now;
                _resetTotal = null;
                return rate;
            }
            if (!_resetTotal.HasValue || total < _resetTotal)
            {
                _resetTotal = total;
                _resetAt = now;
            }
            return 0;
        }

        _resetTotal = null;
        if (total == _total) return 0;
        var result = Rate(total.Value - _total.Value, now - _at);
        _total = total;
        _at = now;
        return result;
    }

    public void ConfirmIdle(DateTimeOffset now)
    {
        _at = now;
        if (_resetTotal.HasValue) _resetAt = now;
    }

    private static long Rate(long bytes, TimeSpan interval)
    {
        var rate = bytes / interval.TotalSeconds;
        return rate >= long.MaxValue ? long.MaxValue : (long)Math.Round(rate);
    }
}
