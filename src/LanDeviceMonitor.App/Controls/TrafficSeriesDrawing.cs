using System.Windows.Media;
using LanDeviceMonitor.Models;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Color = System.Windows.Media.Color;

namespace LanDeviceMonitor.Controls;

// Geometry stays in seconds/KiB. Viewport changes update its transform, while
// only samples, projection aspect, or an active control transition rebuild paths.
internal sealed class TrafficSeriesDrawing
{
    internal const double StrokeThickness = 2.5;
    private readonly bool _isUpload;
    private readonly GeometryGroup _line;
    private readonly GeometryGroup _area;
    private readonly MatrixTransform _transform;
    private Dictionary<(DateTimeOffset, DateTimeOffset), ControlMotion> _motions = new();
    private Point[] _points = [];
    private Point[][] _runs = [];
    private DateTimeOffset[] _sampleKeys = [];
    private EChartsCurve.Segment[] _currentSegments = [];
    private DateTimeOffset _origin, _motionOrigin;
    private double _scaleX = 1, _scaleY = 1;
    private bool _samplesDirty, _projectionDirty;
    private double _preparedTime = double.NaN;
    private bool _dirty;
    private bool _animating;
    private Pen? _pen;
    private LinearGradientBrush? _fill;
    private Color _color;
    private double _top, _bottom;

    internal TrafficSeriesDrawing(bool isUpload, MatrixTransform transform)
    {
        _isUpload = isUpload;
        _transform = transform;
        _line = new GeometryGroup { Transform = transform };
        _area = new GeometryGroup { Transform = transform };
    }

    internal void Reset() { _motions.Clear(); _samplesDirty = true; _dirty = true; }

    internal void SetProjection(double x, double y)
    {
        if (Math.Abs(x / _scaleX - 1) < 1e-6 && Math.Abs(y / _scaleY - 1) < 1e-6) return;
        _scaleX = x; _scaleY = y; _projectionDirty = true; _dirty = true;
    }

    internal void SetSamples(IReadOnlyList<TrafficSample> samples, DateTimeOffset origin, double now, double duration)
    {
        var real = samples.Where(s => _isUpload ? s.HasUploadSample : s.HasDownloadSample).ToArray();
        _sampleKeys = real.Select(s => s.ReceivedAt ?? s.Timestamp).ToArray();
        _points = real.Select(s => new Point((s.Timestamp - origin).TotalSeconds,
            -(_isUpload ? s.UploadBytesPerSecond : s.DownloadBytesPerSecond) / 1024d)).ToArray();
        var runs = new List<Point[]>();
        var run = new List<Point>();
        foreach (var sample in samples)
        {
            if (sample.StartsNewSegment && run.Count > 0) { runs.Add(run.ToArray()); run.Clear(); }
            if (_isUpload ? sample.HasUploadSample : sample.HasDownloadSample)
                run.Add(new Point((sample.Timestamp - origin).TotalSeconds,
                    -(_isUpload ? sample.UploadBytesPerSecond : sample.DownloadBytesPerSecond) / 1024d));
        }
        if (run.Count > 0) runs.Add(run.ToArray());
        _runs = runs.ToArray();
        _origin = origin;
        _samplesDirty = true; _dirty = true;
    }

    private void PrepareFrame(double now, double duration)
    {
        if (!_dirty && _preparedTime == now) return;
        _preparedTime = now;
        if (_dirty || _animating)
        {
            if (_samplesDirty || _projectionDirty)
            {
                var targets = _runs.SelectMany(run => EChartsCurve.Build(run, _scaleX, _scaleY));
                var next = new Dictionary<(DateTimeOffset, DateTimeOffset), ControlMotion>();
                var shift = (_motionOrigin - _origin).TotalSeconds;
                foreach (var target in targets)
                {
                    var key = (SampleKey(target.Start.X), SampleKey(target.End.X));
                    var exists = _motions.TryGetValue(key, out var old);
                    var motion = exists ? old! : new ControlMotion(target, now);
                    if (exists && _samplesDirty) motion.Retarget(target, shift, now, duration);
                    else motion.Target = target; // Reprojection does not restart sample motion.
                    next.Add(key, motion);
                }
                _motions = next; _motionOrigin = _origin;
                _samplesDirty = false; _projectionDirty = false;
            }
            _line.Children.Clear(); _area.Children.Clear();
            _currentSegments = _motions.Values.Select(m => m.Read(now, duration)).ToArray();
            if (_points.Length > 1)
            {
                _line.Children.Add(TrafficChart.BuildSampleGeometry(_points, 0, false, _currentSegments));
                _area.Children.Add(TrafficChart.BuildSampleGeometry(_points, 0, true, _currentSegments));
            }
            _animating = duration > 0 && _motions.Values.Any(m => m.IsAnimating(now, duration));
            _dirty = false;
        }
    }

    private DateTimeOffset SampleKey(double x)
    {
        var left = 0; var right = _points.Length - 1;
        while (left < right) { var middle = (left + right) / 2; if (_points[middle].X < x) left = middle + 1; else right = middle; }
        if (left > 0 && Math.Abs(_points[left - 1].X - x) < Math.Abs(_points[left].X - x)) left--;
        return _sampleKeys[left];
    }

    internal SampleCurve.Bounds? VisibleBounds(double start, double end, double now, double duration)
    {
        PrepareFrame(now, duration);
        return SampleCurve.VisibleBounds(_points, _currentSegments, start, end);
    }

    internal void Draw(DrawingContext dc, Brush brush, double top, double bottom, double now, double duration)
    {
        PrepareFrame(now, duration);
        if (_points.Length == 0) return;
        var color = ((SolidColorBrush)brush).Color;
        if (_pen is null || color != _color || top != _top || bottom != _bottom)
        {
            _color = color; _top = top; _bottom = bottom;
            var stroke = new SolidColorBrush(color) { Opacity = .9 }; stroke.Freeze();
            _pen = new Pen(stroke, StrokeThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            _pen.Freeze();
            _fill = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, top), EndPoint = new Point(0, bottom)
            };
            _fill.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(color.A * .15), color.R, color.G, color.B), 0));
            _fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
            _fill.Freeze();
        }
        dc.DrawGeometry(_fill, null, _area);
        var latest = _transform.Transform(_points[^1]);
        if (_points.Length == 1)
            dc.DrawEllipse(brush, null, latest, 2.5, 2.5);
        else
            dc.DrawGeometry(null, _pen, _line);
        if (_points.Length > 1)
            foreach (var run in _runs.Where(run => run.Length == 1))
                dc.DrawEllipse(brush, null, _transform.Transform(run[0]), 2.5, 2.5);
    }

    private sealed class ControlMotion(EChartsCurve.Segment target, double started)
    {
        internal EChartsCurve.Segment Target = target;
        private Point _firstOffset, _secondOffset;
        private double _started = started;
        internal void Retarget(EChartsCurve.Segment next, double shift, double now, double duration)
        {
            var current = Read(now, duration);
            _firstOffset = new(current.First.X + shift - next.First.X, current.First.Y - next.First.Y);
            _secondOffset = new(current.Second.X + shift - next.Second.X, current.Second.Y - next.Second.Y);
            Target = next; _started = now;
        }
        internal bool IsAnimating(double now, double duration) => now - _started < duration &&
            (_firstOffset != new Point() || _secondOffset != new Point());
        internal EChartsCurve.Segment Read(double now, double duration)
        {
            var remaining = new ChartValueMotion(1, 0, _started).Read(now, duration);
            return Target with {
                First = EChartsCurve.Clamp(new(Target.First.X + _firstOffset.X * remaining, Target.First.Y + _firstOffset.Y * remaining), Target.Start, Target.End),
                Second = EChartsCurve.Clamp(new(Target.Second.X + _secondOffset.X * remaining, Target.Second.Y + _secondOffset.Y * remaining), Target.Start, Target.End)
            };
        }
    }
}
