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
    private readonly MatrixTransform _geometryTransform = new();
    private readonly TranslateTransform _timelineTranslation = new();
    private readonly DrawingGroup _lineDrawing = new();
    private readonly DrawingGroup _areaDrawing = new();
    private readonly DrawingGroup _drawing = new();
    private readonly DrawingGroup _dotsDrawing = new();
    private readonly bool _externalTranslation;
    internal Drawing Drawing => _drawing;
    private Dictionary<(DateTimeOffset, DateTimeOffset), ControlMotion> _motions = new();
    private Point[] _points = [];
    private Point[][] _runs = [];
    private DateTimeOffset[] _sampleKeys = [];
    private EChartsCurve.Segment[] _currentSegments = [];
    private DateTimeOffset _origin, _motionOrigin;
    private double _scaleX = 1, _scaleY = 1;
    private double? _curveScaleY;
    private bool _samplesDirty, _projectionDirty;
    private double _preparedTime = double.NaN;
    private bool _dirty;
    private bool _animating;
    private bool _geometryDirty;
    private double _visibleStart = double.NegativeInfinity, _visibleEnd = double.PositiveInfinity;
    private int _firstDrawn = -1, _lastDrawn = -1;
    private Pen? _pen;
    private LinearGradientBrush? _fill;
    private Color _color;
    private double _top, _bottom;

    internal TrafficSeriesDrawing(bool isUpload, MatrixTransform transform, bool externalTranslation = false)
    {
        _isUpload = isUpload;
        _transform = transform;
        _externalTranslation = externalTranslation;
        _line = new GeometryGroup { Transform = _geometryTransform };
        _area = new GeometryGroup { Transform = _geometryTransform };
        _lineDrawing.Transform = _timelineTranslation;
        _areaDrawing.Transform = _timelineTranslation;
        _drawing.Children.Add(_areaDrawing);
        _drawing.Children.Add(_lineDrawing);
        _drawing.Children.Add(_dotsDrawing);
    }

    internal void Reset() { _curveScaleY = null; _motions.Clear(); _samplesDirty = true; _dirty = true; }

    internal void SetProjection(double x, double y)
    {
        var reflow = Math.Abs(x / _scaleX - 1) >= 1e-6;
        _scaleX = x; _scaleY = y;
        // Keep ECharts' calculation metric stable during automatic Y scaling.
        // Explicit horizontal layout/range changes can reflow the whole curve.
        if (reflow) { if (_points.Length > 1) _curveScaleY = y; _projectionDirty = true; _dirty = true; }
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
                if (_points.Length > 1) _curveScaleY ??= _scaleY;
                var targets = _runs.SelectMany(run => EChartsCurve.Build(run, _scaleX, _curveScaleY ?? _scaleY))
                    .Select(s => s with { Start = _points[SampleIndex(s.Start.X)], End = _points[SampleIndex(s.End.X)] });
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
            _currentSegments = _motions.Values.Select(m => m.Read(now, duration)).ToArray();
            _geometryDirty = true;
            _animating = duration > 0 && _motions.Values.Any(m => m.IsAnimating(now, duration));
            _dirty = false;
        }
        UpdateVisibleGeometry();
    }

    private void UpdateVisibleGeometry()
    {
        var first = 0;
        while (first < _currentSegments.Length && _currentSegments[first].End.X < _visibleStart) first++;
        first = Math.Max(0, first - 1);
        var last = first;
        while (last < _currentSegments.Length && _currentSegments[last].Start.X <= _visibleEnd) last++;
        last = Math.Min(_currentSegments.Length, last + 1);
        if (!_geometryDirty && first == _firstDrawn && last == _lastDrawn) return;
        _geometryDirty = false; _firstDrawn = first; _lastDrawn = last;
        _line.Children.Clear(); _area.Children.Clear();
        if (_points.Length <= 1) return;
        // Preserve the original controls, including neighboring segments at
        // both edges. Hidden history stays available for range/hover queries
        // without sending all ten minutes through the rasterizer each frame.
        var segments = _currentSegments.AsSpan(first, last - first).ToArray();
        _line.Children.Add(TrafficChart.BuildSampleGeometry(_points, 0, false, segments));
        _area.Children.Add(TrafficChart.BuildSampleGeometry(_points, 0, true, segments));
    }

    private DateTimeOffset SampleKey(double x) => _sampleKeys[SampleIndex(x)];

    private int SampleIndex(double x)
    {
        var left = 0; var right = _points.Length - 1;
        while (left < right) { var middle = (left + right) / 2; if (_points[middle].X < x) left = middle + 1; else right = middle; }
        if (left > 0 && Math.Abs(_points[left - 1].X - x) < Math.Abs(_points[left].X - x)) left--;
        return left;
    }

    internal SampleCurve.Bounds? VisibleBounds(double start, double end, double now, double duration)
    {
        _visibleStart = start; _visibleEnd = end;
        PrepareFrame(now, duration);
        UpdateVisibleGeometry();
        return SampleCurve.VisibleBounds(_points, _currentSegments, start, end);
    }

    internal void Draw(DrawingContext dc, Brush brush, double top, double bottom, double now, double duration)
    {
        UpdateDrawing(brush, top, bottom, now, duration);
        dc.DrawDrawing(_drawing);
    }

    internal void UpdateDrawing(Brush brush, double top, double bottom, double now, double duration)
    {
        PrepareFrame(now, duration);
        if (_points.Length == 0)
        {
            if (_dotsDrawing.Children.Count > 0) _dotsDrawing.Children.Clear();
            return;
        }
        // Scrolling changes only a drawing's translation. Putting that changing
        // offset on Geometry.Transform dirties both paths and retessellates all
        // ten minutes of curves on every frame, even with unchanged samples.
        var matrix = _transform.Matrix;
        _timelineTranslation.X = _externalTranslation ? 0 : matrix.OffsetX;
        matrix.OffsetX = 0;
        if (_geometryTransform.Matrix != matrix) _geometryTransform.Matrix = matrix;
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
            _lineDrawing.Children.Clear();
            _lineDrawing.Children.Add(new GeometryDrawing(null, _pen, _line));
            _areaDrawing.Children.Clear();
            _areaDrawing.Children.Add(new GeometryDrawing(_fill, null, _area));
        }
        if (_points.Length == 1 || _runs.Any(run => run.Length == 1) || _dotsDrawing.Children.Count > 0)
        {
            var pointMatrix = _transform.Matrix;
            if (_externalTranslation) pointMatrix.OffsetX = 0;
            using var dc = _dotsDrawing.Open();
            foreach (var run in _runs.Where(run => run.Length == 1))
                dc.DrawEllipse(brush, null, pointMatrix.Transform(run[0]), 2.5, 2.5);
        }
    }

    private sealed class ControlMotion(EChartsCurve.Segment target, double started)
    {
        internal EChartsCurve.Segment Target = target;
        private Point _firstOffset, _secondOffset;
        private double _started = started;
        internal void Retarget(EChartsCurve.Segment next, double shift, double now, double duration)
        {
            // A new UI snapshot can contain the same samples. Preserve the
            // current timeline instead of restarting a long transition.
            bool Same(Point a, Point b) => Math.Abs(a.X + shift - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;
            if (Same(Target.Start, next.Start) && Same(Target.First, next.First) &&
                Same(Target.Second, next.Second) && Same(Target.End, next.End))
            { Target = next; return; }
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
