namespace LanDeviceMonitor.Controls;

internal readonly record struct ChartHoverFrame(double X, double UploadY, double DownloadY, double TooltipX, double TooltipY);

// Only the overlay's presentation moves. Tooltip times and rates stay measured.
internal sealed class ChartHoverMotion
{
    internal const double DurationSeconds = .16;
    private bool _initialized, _hasUpload, _hasDownload;
    private long _key;
    private ChartHoverFrame _target, _offset;
    private ChartValueMotion _remaining = new(0, 0, 0);

    internal ChartHoverFrame Update(long key, ChartHoverFrame target, bool hasUpload, bool hasDownload,
        double now, double duration)
    {
        if (!_initialized || duration <= 0)
        {
            _offset = default;
            _remaining = new(0, 0, now);
        }
        else if (key != _key)
        {
            var current = Read(now, duration);
            _offset = new(current.X - target.X,
                _hasUpload && hasUpload ? current.UploadY - target.UploadY : 0,
                _hasDownload && hasDownload ? current.DownloadY - target.DownloadY : 0,
                current.TooltipX - target.TooltipX, current.TooltipY - target.TooltipY);
            _remaining = new(1, 0, now);
        }
        _key = key; _target = target; _initialized = true;
        _hasUpload = hasUpload; _hasDownload = hasDownload;
        return Read(now, duration);
    }

    internal ChartHoverFrame Read(double now, double duration)
    {
        var remaining = _remaining.Read(now, duration);
        return new(_target.X + _offset.X * remaining,
            _target.UploadY + _offset.UploadY * remaining,
            _target.DownloadY + _offset.DownloadY * remaining,
            _target.TooltipX + _offset.TooltipX * remaining,
            _target.TooltipY + _offset.TooltipY * remaining);
    }

    internal void Reset() => _initialized = false;
}
