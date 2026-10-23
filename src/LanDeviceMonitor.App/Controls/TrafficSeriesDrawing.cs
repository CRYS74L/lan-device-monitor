using System.Windows.Media;
using LanDeviceMonitor.Models;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Color = System.Windows.Media.Color;

namespace LanDeviceMonitor.Controls;

// Geometry stays in seconds/KiB. Viewport changes update its transform, while
// only new samples or an active tangent transition rebuild the frozen paths.
internal sealed class TrafficSeriesDrawing
{
    internal const double StrokeThickness = 2.5;
    private readonly bool _isUpload;
    private readonly GeometryGroup _line;
    private readonly GeometryGroup _area;
    private readonly MatrixTransform _transform;
    private Dictionary<DateTimeOffset, ChartValueMotion> _motions = new();
    private Point[] _points = [];
    private ChartValueMotion[] _slopes = [];
    private double[] _currentSlopes = [];
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

    internal void Reset() => _motions.Clear();

    internal void SetSamples(IReadOnlyList<TrafficSample> samples, DateTimeOffset origin, double now, double duration)
    {
        var real = samples.Where(s => _isUpload ? s.HasUploadSample : s.HasDownloadSample).ToArray();
        _points = real.Select(s => new Point((s.Timestamp - origin).TotalSeconds,
            -(_isUpload ? s.UploadBytesPerSecond : s.DownloadBytesPerSecond) / 1024d)).ToArray();
        var targets = SampleCurve.Slopes(_points);
        var motions = new Dictionary<DateTimeOffset, ChartValueMotion>();
        _slopes = new ChartValueMotion[real.Length];
        for (var i = 0; i < real.Length; i++)
        {
            var motion = _motions.TryGetValue(real[i].Timestamp, out var old)
                ? old.Retarget(targets[i], now, duration) : new(targets[i], targets[i], now);
            _slopes[i] = motion;
            motions.Add(real[i].Timestamp, motion);
        }
        _motions = motions;
        _dirty = true;
    }

    private void PrepareFrame(double now, double duration)
    {
        if (!_dirty && _preparedTime == now) return;
        _preparedTime = now;
        if (_dirty || _animating)
        {
            _line.Children.Clear(); _area.Children.Clear();
            _currentSlopes = _slopes.Select(m => m.Read(now, duration)).ToArray();
            if (_points.Length > 1)
            {
                _line.Children.Add(TrafficChart.BuildSampleGeometry(_points, 0, false, _currentSlopes));
                _area.Children.Add(TrafficChart.BuildSampleGeometry(_points, 0, true, _currentSlopes));
            }
            _animating = duration > 0 && _slopes.Any(m => m.From != m.To && now - m.Started < duration);
            _dirty = false;
        }
    }

    internal SampleCurve.Bounds? VisibleBounds(double start, double end, double now, double duration)
    {
        PrepareFrame(now, duration);
        return SampleCurve.VisibleBounds(_points, _currentSlopes, start, end);
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
    }
}
