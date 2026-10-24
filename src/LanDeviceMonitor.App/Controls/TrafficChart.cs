using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace LanDeviceMonitor.Controls;

/// <summary>
/// 按真实采样时间绘制曲线，使用连续推进的固定时长视口。
/// </summary>
public sealed class TrafficChart : FrameworkElement
{
    private static readonly Brush FallbackGridBrush = CreateBrush(0x55, 0x9A, 0xA2, 0xB2);
    private static readonly Brush FallbackLabelBrush = CreateBrush(0xFF, 0x7C, 0x86, 0x98);
    private static readonly Brush FallbackUploadBrush = CreateBrush(0xFF, 0xFF, 0x96, 0x72);
    private static readonly Brush FallbackDownloadBrush = CreateBrush(0xFF, 0x0B, 0x80, 0xFF);

    public static readonly DependencyProperty SamplesProperty =
        DependencyProperty.Register(
            nameof(Samples),
            typeof(IReadOnlyList<TrafficSample>),
            typeof(TrafficChart),
            new FrameworkPropertyMetadata(
                Array.Empty<TrafficSample>(),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnSamplesChanged));

    public static readonly DependencyProperty GridBrushProperty =
        DependencyProperty.Register(
            nameof(GridBrush),
            typeof(Brush),
            typeof(TrafficChart),
            new FrameworkPropertyMetadata(
                FallbackGridBrush,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty =
        DependencyProperty.Register(
            nameof(LabelBrush),
            typeof(Brush),
            typeof(TrafficChart),
            new FrameworkPropertyMetadata(
                FallbackLabelBrush,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UploadBrushProperty =
        DependencyProperty.Register(
            nameof(UploadBrush),
            typeof(Brush),
            typeof(TrafficChart),
            new FrameworkPropertyMetadata(
                FallbackUploadBrush,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DownloadBrushProperty =
        DependencyProperty.Register(
            nameof(DownloadBrush),
            typeof(Brush),
            typeof(TrafficChart),
            new FrameworkPropertyMetadata(
                FallbackDownloadBrush,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RangeMinutesProperty =
        DependencyProperty.Register(
            nameof(RangeMinutes),
            typeof(int),
            typeof(TrafficChart),
            new FrameworkPropertyMetadata(
                1,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnRangeChanged));

    public static readonly DependencyProperty AnimationMillisecondsProperty =
        DependencyProperty.Register(
            nameof(AnimationMilliseconds), typeof(double), typeof(TrafficChart),
            new FrameworkPropertyMetadata(220d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SampleIntervalSecondsProperty =
        DependencyProperty.Register(
            nameof(SampleIntervalSeconds), typeof(double), typeof(TrafficChart),
            new FrameworkPropertyMetadata(5d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DeviceKeyProperty =
        DependencyProperty.Register(
            nameof(DeviceKey),
            typeof(string),
            typeof(TrafficChart),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnDeviceKeyChanged));

    private int _hoveredIndex = -1;
    private readonly ChartHoverMotion _hoverMotion = new();
    private IReadOnlyList<TrafficSample> _renderSamples = Array.Empty<TrafficSample>();
    private bool _hasRenderSamples;
    private bool _sourceChanged = true;
    private readonly Dictionary<DateTimeOffset, int> _sampleIndexes = new();
    private readonly MatrixTransform _plotTransform = new();
    private readonly TrafficSeriesDrawing _downloadSeries;
    private readonly TrafficSeriesDrawing _uploadSeries;
    private DateTimeOffset _sampleOrigin, _presentationTime;
    private TrafficScale _targetScale;
    private readonly Dictionary<(string, double, Color, TextAlignment, bool), FormattedText> _textCache = new();
    private Typeface? _regularTypeface, _boldTypeface;
    private DateTimeOffset? _hoveredTimestamp;
    private readonly ChartTimeline _presentationClock = new();
    private ChartValueMotion _rangeMotion = new(1, 1, 0);
    private ChartValueMotion? _scaleTopMotion;
    private ChartValueMotion? _scaleBottomMotion;
    private double _presentationRangeMinutes = 1;

    private static double MotionTime =>
        System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

    private double MotionDurationSeconds =>
        IsLoaded && SystemParameters.ClientAreaAnimation ? Math.Max(0, AnimationMilliseconds) / 1000 : 0;

    public TrafficChart()
    {
        _downloadSeries = new(false, _plotTransform);
        _uploadSeries = new(true, _plotTransform);
        Loaded += (_, _) => { CompositionTarget.Rendering -= RenderFrame; CompositionTarget.Rendering += RenderFrame; };
        Unloaded += (_, _) => CompositionTarget.Rendering -= RenderFrame;
    }

    private void RenderFrame(object? sender, EventArgs args)
    {
        if (IsVisible) InvalidateVisual();
    }

    private static void OnSamplesChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var chart = (TrafficChart)dependencyObject;
        chart._sourceChanged = true;
        chart.InvalidateVisual();
    }

    private static void OnRangeChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var chart = (TrafficChart)dependencyObject;
        var range = NormalizeRange((int)args.NewValue);
        chart._rangeMotion = chart._rangeMotion.Retarget(range, MotionTime, chart.MotionDurationSeconds);
        chart._hoveredIndex = -1;
        chart._hoveredTimestamp = null;
        chart._hoverMotion.Reset();
        chart.InvalidateVisual();
    }

    private static void OnDeviceKeyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var chart = (TrafficChart)dependencyObject;
        chart._renderSamples = Array.Empty<TrafficSample>();
        chart._hasRenderSamples = false;
        chart._hoveredIndex = -1;
        chart._hoveredTimestamp = null;
        chart._hoverMotion.Reset();
        chart._presentationClock.Reset();
        chart._sourceChanged = true;
        chart._downloadSeries.Reset();
        chart._uploadSeries.Reset();
        chart._scaleTopMotion = null;
        chart._scaleBottomMotion = null;
        var range = NormalizeRange(chart.RangeMinutes);
        chart._rangeMotion = new(range, range, 0);
        chart.InvalidateVisual();
    }

    private void PrepareSamples(double now, double duration)
    {
        if (!_sourceChanged) return;
        _renderSamples = (Samples ?? Array.Empty<TrafficSample>()).Where(s => !s.IsPlaceholder)
            .GroupBy(s => s.Timestamp).Select(g => g.Last()).OrderBy(s => s.Timestamp).ToArray();
        _sampleIndexes.Clear();
        for (var i = 0; i < _renderSamples.Count; i++) _sampleIndexes.Add(_renderSamples[i].ReceivedAt ?? _renderSamples[i].Timestamp, i);
        _sampleOrigin = _renderSamples.Count == 0 ? DateTimeOffset.Now : _renderSamples[0].Timestamp;
        _downloadSeries.SetSamples(_renderSamples, _sampleOrigin, now, duration);
        _uploadSeries.SetSamples(_renderSamples, _sampleOrigin, now, duration);
        _hasRenderSamples = true;
        _sourceChanged = false;
    }

    public IReadOnlyList<TrafficSample> Samples
    {
        get => (IReadOnlyList<TrafficSample>)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public Brush LabelBrush
    {
        get => (Brush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public Brush UploadBrush
    {
        get => (Brush)GetValue(UploadBrushProperty);
        set => SetValue(UploadBrushProperty, value);
    }

    public Brush DownloadBrush
    {
        get => (Brush)GetValue(DownloadBrushProperty);
        set => SetValue(DownloadBrushProperty, value);
    }

    public int RangeMinutes
    {
        get => (int)GetValue(RangeMinutesProperty);
        set => SetValue(RangeMinutesProperty, value);
    }

    public double AnimationMilliseconds
    {
        get => (double)GetValue(AnimationMillisecondsProperty);
        set => SetValue(AnimationMillisecondsProperty, value);
    }

    public double SampleIntervalSeconds
    {
        get => (double)GetValue(SampleIntervalSecondsProperty);
        set => SetValue(SampleIntervalSecondsProperty, value);
    }

    public string DeviceKey
    {
        get => (string?)GetValue(DeviceKeyProperty) ?? string.Empty;
        set => SetValue(DeviceKeyProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 100 || height < 80)
        {
            return;
        }

        GetPlotBounds(width, height, out var left, out var right, out var top, out var bottom);
        // FrameworkElement otherwise only receives input on its painted geometry.
        drawingContext.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(left, top, right - left, bottom - top));
        var rangeMinutes = NormalizeRange(RangeMinutes);
        var frameTime = MotionTime;
        var duration = MotionDurationSeconds;
        _presentationRangeMinutes = _rangeMotion.Read(frameTime, duration);
        PrepareSamples(frameTime, duration);
        var source = _renderSamples;
        var now = DateTimeOffset.Now;
        _presentationClock.Observe(now.ToUnixTimeMilliseconds() / 1000d,
            SampleIntervalSeconds, frameTime);
        now = DateTimeOffset.FromUnixTimeMilliseconds((long)(_presentationClock.Read(frameTime) * 1000)).ToOffset(now.Offset);
        _presentationTime = now;
        if (_hoveredTimestamp is { } hovered)
            _hoveredIndex = _sampleIndexes.GetValueOrDefault(hovered, -1);

        var visibleStart = now.AddSeconds(-_presentationRangeMinutes * 60);
        var scaleX = (right - left) / (_presentationRangeMinutes * 60);
        var initialScale = _scaleTopMotion is null || _scaleBottomMotion is null
            ? CalculateScale(source.Where(s => s.Timestamp >= visibleStart && s.Timestamp <= now).ToArray()) : default;
        var previousSpan = Math.Max(1, (_scaleTopMotion?.Read(frameTime, duration) ?? initialScale.TopValue) -
            (_scaleBottomMotion?.Read(frameTime, duration) ?? initialScale.BottomValue));
        var projectionY = (bottom - top) * 1024 / previousSpan;
        _downloadSeries.SetProjection(scaleX, projectionY);
        _uploadSeries.SetProjection(scaleX, projectionY);
        _targetScale = CalculateVisibleScale((visibleStart - _sampleOrigin).TotalSeconds,
            (now - _sampleOrigin).TotalSeconds, frameTime, duration);
        var targetScale = _targetScale;
        // Expand with the currently visible rise, never with an off-screen peak.
        _scaleTopMotion = _scaleTopMotion is not { } oldTop ||
            targetScale.TopValue > oldTop.Read(frameTime, duration)
                ? new(targetScale.TopValue, targetScale.TopValue, frameTime)
                : oldTop.Retarget(targetScale.TopValue, frameTime, duration);
        _scaleBottomMotion = _scaleBottomMotion is not { } oldBottom ||
            targetScale.BottomValue < oldBottom.Read(frameTime, duration)
                ? new(targetScale.BottomValue, targetScale.BottomValue, frameTime)
                : oldBottom.Retarget(targetScale.BottomValue, frameTime, duration);
        var scale = new TrafficScale(_scaleTopMotion.Value.Read(frameTime, duration),
            _scaleBottomMotion.Value.Read(frameTime, duration));
        var scaleY = (bottom - top) * 1024 / Math.Max(1, scale.TopValue - scale.BottomValue);
        _plotTransform.Matrix = new Matrix(scaleX, 0, 0, scaleY,
            right - (now - _sampleOrigin).TotalSeconds * scaleX,
            bottom + scale.BottomValue / 1024 * scaleY);

        DrawGrid(drawingContext, left, right, top, bottom);
        DrawAxisLabels(drawingContext, left, top, bottom, scale);
        DrawTimeAxis(
            drawingContext,
            left,
            right,
            bottom,
            visibleStart, now,
            rangeMinutes);
        if (source.Count > 0)
        {
            var clip = new Rect(left, top, right - left, bottom - top);
            var strokePadding = TrafficSeriesDrawing.StrokeThickness / 2 + .5;
            clip.Inflate(strokePadding, strokePadding);
            drawingContext.PushClip(new RectangleGeometry(clip));
            // Clash Verge 先画下载线，再画上传线，让前景线在交叉处保持清晰。
            _downloadSeries.Draw(drawingContext, DownloadBrush, top, bottom, frameTime, duration);
            _uploadSeries.Draw(drawingContext, UploadBrush, top, bottom, frameTime, duration);
            drawingContext.Pop();
        }

        if (_hoveredIndex >= 0 && _hoveredIndex < source.Count)
        {
            DrawHoverState(
                drawingContext,
                source,
                left,
                right,
                top,
                bottom,
                scale,
                _hoveredIndex,
                visibleStart, _presentationRangeMinutes * 60);
        }
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHover(e.GetPosition(this));
    }

    internal void UpdateHover(Point position)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 100 || height < 80)
        {
            ClearHover();
            return;
        }

        GetPlotBounds(width, height, out var left, out var right, out var top, out var bottom);
        if (position.X < left || position.X > right || position.Y < top || position.Y > bottom)
        {
            ClearHover();
            return;
        }

        var samples = _renderSamples;
        if (!_hasRenderSamples || samples.Count == 0)
        {
            ClearHover();
            return;
        }

        var target = _presentationTime.AddSeconds((position.X - right) / (right - left) * _presentationRangeMinutes * 60);
        var selected = FindHoverSample(samples, target, _presentationTime.AddMinutes(-_presentationRangeMinutes),
            _presentationTime, SampleIntervalSeconds);
        if (selected is not { } index)
        {
            ClearHover();
            return;
        }
        var changed = _hoveredIndex != index;
        _hoveredIndex = index;
        _hoveredTimestamp = samples[index].ReceivedAt ?? samples[index].Timestamp;
        if (changed)
        {
            InvalidateVisual();
        }
    }

    internal static int? FindHoverSample(IReadOnlyList<TrafficSample> samples, DateTimeOffset target,
        DateTimeOffset start, DateTimeOffset end, double intervalSeconds)
    {
        if (target < start || target > end) return null;
        int? selected = null;
        var distance = double.PositiveInfinity;
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            if (sample.IsPlaceholder || (!sample.HasUploadSample && !sample.HasDownloadSample) ||
                sample.Timestamp < start || sample.Timestamp > end) continue;
            var candidate = Math.Abs((sample.Timestamp - target).TotalSeconds);
            if (candidate < distance) { selected = i; distance = candidate; }
        }
        // Normal sample columns meet at their midpoints; interruptions stay unknown.
        return distance <= Math.Max(.001, selected is { } index ? samples[index].IntervalSeconds ?? intervalSeconds : intervalSeconds) / 2 ? selected : null;
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ClearHover();
    }

    private void ClearHover()
    {
        _hoverMotion.Reset();
        if (_hoveredIndex < 0)
        {
            return;
        }

        _hoveredIndex = -1;
        _hoveredTimestamp = null;
        InvalidateVisual();
    }

    private static IReadOnlyList<TrafficSample> GetVisibleSamples(
        IReadOnlyList<TrafficSample> allSamples, int rangeMinutes, DateTimeOffset? now = null)
    {
        if (allSamples.Count == 0) return Array.Empty<TrafficSample>();
        var end = now ?? allSamples[^1].Timestamp;
        var start = end.AddMinutes(-NormalizeRange(rangeMinutes));
        var first = 0;
        while (first < allSamples.Count && allSamples[first].Timestamp < start) first++;
        if (first == allSamples.Count) return Array.Empty<TrafficSample>();
        // Keep the left curve's controls, without allocating an entry for every
        // elapsed second when a window resumes with an old cached snapshot.
        return allSamples.Skip(Math.Max(0, first - 2)).Where(sample => sample.Timestamp <= end).ToArray();
    }
    private static int NormalizeRange(int rangeMinutes) =>
        rangeMinutes is 1 or 5 or 10 ? rangeMinutes : 1;

    internal static int GetFixedSlotCount(int rangeMinutes) =>
        NormalizeRange(rangeMinutes) * 60 + 1;

    internal static int GetTimeAxisMaximumLabelCount(int rangeMinutes) =>
        NormalizeRange(rangeMinutes) == 10 ? 8 : 6;

    internal static double GetPlotLeftInset() => 60d;

    private static void GetPlotBounds(
        double width,
        double height,
        out double left,
        out double right,
        out double top,
        out double bottom)
    {
        left = GetPlotLeftInset();
        right = Math.Max(left + 1d, width - 16d);
        top = 23d;
        bottom = Math.Max(top + 1d, height - 32d);
    }

    private void DrawGrid(
        DrawingContext drawingContext,
        double left,
        double right,
        double top,
        double bottom)
    {
        var gridPen = new Pen(WithOpacity(GridBrush, 0.62), 0.7);
        for (var index = 0; index <= 4; index++)
        {
            var y = top + (bottom - top) * index / 4d;
            drawingContext.DrawLine(gridPen, new Point(left, y), new Point(right, y));
        }

        for (var index = 0; index <= 6; index++)
        {
            var x = left + (right - left) * index / 6d;
            drawingContext.DrawLine(gridPen, new Point(x, top), new Point(x, bottom));
        }
    }

    private void DrawAxisLabels(
        DrawingContext drawingContext,
        double left,
        double top,
        double bottom,
        TrafficScale scale)
    {
        var axisBrush = WithOpacity(LabelBrush, 0.9);
        DrawText(
            drawingContext,
            FormatRate(scale.TopValue),
            new Point(left - 7, top - 7),
            10,
            axisBrush,
            TextAlignment.Right);
        DrawText(
            drawingContext,
            FormatRate((scale.TopValue + scale.BottomValue) / 2d),
            new Point(left - 7, (top + bottom) / 2d - 7),
            10,
            axisBrush,
            TextAlignment.Right);
        DrawText(
            drawingContext,
            FormatRate(scale.BottomValue),
            new Point(left - 7, bottom - 7),
            10,
            axisBrush,
            TextAlignment.Right);
    }

    internal static IReadOnlyList<(DateTimeOffset Timestamp, double X)> GetTimeAxisTicks(
        DateTimeOffset start, DateTimeOffset end, int rangeMinutes, double left, double right)
    {
        var minimumSpacing = rangeMinutes == 1 ? 35d : rangeMinutes == 5 ? 38d : 40d;
        var count = Math.Min(GetTimeAxisMaximumLabelCount(rangeMinutes),
            Math.Max(1, (int)Math.Floor((right - left) / minimumSpacing) + 1));
        var points = new (DateTimeOffset, double)[count];
        for (var i = 0; i < count; i++)
        {
            var progress = count == 1 ? 0 : i / (double)(count - 1);
            points[i] = (start.AddTicks((long)((end - start).Ticks * progress)), left + (right - left) * progress);
        }
        return points;
    }

    private void DrawTimeAxis(DrawingContext dc, double left, double right, double bottom,
        DateTimeOffset start, DateTimeOffset end, int rangeMinutes)
    {
        var points = GetTimeAxisTicks(start, end, rangeMinutes, left, right);
        var brush = WithOpacity(LabelBrush, .72);
        var format = rangeMinutes == 1 ? "mm:ss" : "HH:mm";
        for (var i = 0; i < points.Count; i++)
        {
            var alignment = i == 0 ? TextAlignment.Left : i == points.Count - 1 ? TextAlignment.Right : TextAlignment.Center;
            DrawText(dc, points[i].Timestamp.ToString(format, CultureInfo.CurrentCulture),
                new Point(points[i].X, bottom + 8), 10, brush, alignment);
        }
    }
    private static IReadOnlyList<IReadOnlyList<int>> GetRenderableSampleSegments(
        IReadOnlyList<TrafficSample> samples,
        bool isUpload)
    {
        var segments = new List<IReadOnlyList<int>>();
        List<int>? currentSegment = null;
        foreach (var (sample, index) in samples.Select((sample, index) => (sample, index)))
        {
            var isRenderable = !sample.IsPlaceholder &&
                (isUpload ? sample.HasUploadSample : sample.HasDownloadSample);
            if (!isRenderable)
            {
                continue;
            }

            if (currentSegment is null)
            {
                currentSegment = new List<int>();
                segments.Add(currentSegment);
            }

            currentSegment.Add(index);
        }

        return segments;
    }

    private void DrawHoverState(
        DrawingContext drawingContext,
        IReadOnlyList<TrafficSample> samples,
        double left,
        double right,
        double top,
        double bottom,
        TrafficScale scale,
        int index,
        DateTimeOffset visibleStart,
        double rangeSeconds)
    {
        var sample = samples[index];
        var x = left + (sample.Timestamp - visibleStart).TotalSeconds / rangeSeconds * (right - left);
        if (x < left || x > right) { _hoverMotion.Reset(); return; }

        DrawHoverOverlay(
            drawingContext,
            sample,
            x,
            top,
            left,
            right,
            bottom,
            GetY(sample.UploadBytesPerSecond, scale, top, bottom),
            GetY(sample.DownloadBytesPerSecond, scale, top, bottom));
    }

    private void DrawHoverPoint(DrawingContext drawingContext, Point point, Brush brush)
    {
        drawingContext.DrawEllipse(GetTooltipBackgroundBrush(), null, point, 5, 5);
        drawingContext.DrawEllipse(brush, null, point, 3.5, 3.5);
    }

    private void DrawHoverOverlay(
        DrawingContext drawingContext,
        TrafficSample sample,
        double pointX,
        double top,
        double left,
        double right,
        double bottom,
        double uploadY,
        double downloadY)
    {
        var isDark = GetColor(GridBrush, 1).R < 160;
        var background = GetTooltipBackgroundBrush();
        var foreground = CreateBrush(
            0xFF,
            isDark ? (byte)0xF2 : (byte)0x18,
            isDark ? (byte)0xF5 : (byte)0x20,
            isDark ? (byte)0xFB : (byte)0x34);
        var titleBrush = WithOpacity(foreground, 0.7);
        var borderPen = new Pen(WithOpacity(LabelBrush, 0.28), 0.5);

        var title = CreateFormattedText(
            sample.Timestamp.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
            10,
            titleBrush,
            TextAlignment.Left);
        var upload = CreateFormattedText(
            sample.HasUploadSample
                ? $"↑ {FormatRate(sample.UploadBytesPerSecond)}/s"
                : "↑ —",
            11,
            UploadBrush,
            TextAlignment.Left,
            isBold: true);
        var download = CreateFormattedText(
            sample.HasDownloadSample
                ? $"↓ {FormatRate(sample.DownloadBytesPerSecond)}/s"
                : "↓ —",
            11,
            DownloadBrush,
            TextAlignment.Left,
            isBold: true);

        const double paddingX = 10;
        const double paddingY = 8;
        const double lineGap = 3;
        var contentWidth = Math.Max(title.Width, Math.Max(upload.Width, download.Width));
        var contentHeight = title.Height + upload.Height + download.Height + lineGap * 2;
        var boxWidth = contentWidth + paddingX * 2;
        var boxHeight = contentHeight + paddingY * 2;
        var boxX = pointX + 12;
        if (boxX + boxWidth > right)
        {
            boxX = pointX - boxWidth - 12;
        }

        var boxY = top + 8;
        if (boxY + boxHeight > bottom)
        {
            boxY = Math.Max(top + 2, bottom - boxHeight - 2);
        }

        var frame = _hoverMotion.Update((sample.ReceivedAt ?? sample.Timestamp).UtcTicks,
            new(pointX, uploadY, downloadY, boxX, boxY), sample.HasUploadSample, sample.HasDownloadSample,
            MotionTime, MotionDurationSeconds > 0 ? ChartHoverMotion.DurationSeconds : 0);
        var indicatorPen = new Pen(WithOpacity(LabelBrush, .72), 1) { DashStyle = new DashStyle([4, 4], 0) };
        drawingContext.DrawLine(indicatorPen, new Point(frame.X, top), new Point(frame.X, bottom));
        if (sample.HasUploadSample) DrawHoverPoint(drawingContext, new Point(frame.X, frame.UploadY), UploadBrush);
        if (sample.HasDownloadSample) DrawHoverPoint(drawingContext, new Point(frame.X, frame.DownloadY), DownloadBrush);
        boxX = Math.Clamp(frame.TooltipX, left, Math.Max(left, right - boxWidth));
        boxY = frame.TooltipY;
        var box = new Rect(boxX, boxY, boxWidth, boxHeight);
        drawingContext.DrawRoundedRectangle(background, borderPen, box, 7, 7);
        var textX = boxX + paddingX;
        var textY = boxY + paddingY;
        drawingContext.DrawText(title, new Point(textX, textY));
        textY += title.Height + lineGap;
        drawingContext.DrawText(upload, new Point(textX, textY));
        textY += upload.Height + lineGap;
        drawingContext.DrawText(download, new Point(textX, textY));
    }

    internal static StreamGeometry BuildSampleGeometry(
        IReadOnlyList<Point> points,
        double baseline,
        bool closeToBaseline, IReadOnlyList<EChartsCurve.Segment> segments)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            Point? first = null, end = null;
            foreach (var segment in segments)
            {
                if (end != segment.Start)
                {
                    CloseRun(); first = segment.Start;
                    context.BeginFigure(segment.Start, closeToBaseline, isClosed: false);
                }
                context.BezierTo(segment.First, segment.Second, segment.End, isStroked: true, isSmoothJoin: true);
                end = segment.End;
            }
            CloseRun();
            void CloseRun()
            {
                if (closeToBaseline && first is { } start && end is { } last)
                {
                    context.LineTo(new Point(last.X, baseline), isStroked: false, isSmoothJoin: false);
                    context.LineTo(new Point(start.X, baseline), isStroked: false, isSmoothJoin: false);
                    context.Close();
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static double GetY(
        long value,
        TrafficScale scale,
        double top,
        double bottom)
    {
        var range = Math.Max(1d, scale.TopValue - scale.BottomValue);
        var normalized = Math.Clamp(
            (Math.Max(scale.BottomValue, value) - scale.BottomValue) / range,
            0d,
            1d);
        return bottom - normalized * (bottom - top);
    }

    private TrafficScale CalculateVisibleScale(double start, double end, double now, double duration)
    {
        var minimum = double.PositiveInfinity; var maximum = double.NegativeInfinity;
        foreach (var series in new[] { _downloadSeries, _uploadSeries })
        {
            if (series.VisibleBounds(start, end, now, duration) is not { } bounds) continue;
            minimum = Math.Min(minimum, -bounds.Maximum * 1024);
            maximum = Math.Max(maximum, -bounds.Minimum * 1024);
        }
        return double.IsPositiveInfinity(minimum) ? new(1024, 0) : ScaleForBounds(minimum, maximum);
    }

    private static TrafficScale CalculateScale(IReadOnlyList<TrafficSample> samples)
    {
        var actualSamples = samples
            .Where(sample =>
                !sample.IsPlaceholder &&
                (sample.HasUploadSample || sample.HasDownloadSample))
            .ToArray();
        if (actualSamples.Length == 0)
        {
            return new TrafficScale(1024d, 0d);
        }

        var maxValue = actualSamples.Max(sample => Math.Max(
            sample.HasUploadSample ? sample.UploadBytesPerSecond : 0,
            sample.HasDownloadSample ? sample.DownloadBytesPerSecond : 0));
        var minValue = actualSamples.Min(sample => Math.Min(
            sample.HasUploadSample ? sample.UploadBytesPerSecond : 0,
            sample.HasDownloadSample ? sample.DownloadBytesPerSecond : 0));
        return ScaleForBounds(minValue, maxValue);
    }

    private static TrafficScale ScaleForBounds(double minValue, double maxValue)
    {
        if (maxValue == 0)
        {
            return new TrafficScale(1024d, 0d);
        }

        var range = maxValue - minValue;
        if (range <= 0)
        {
            return new TrafficScale(maxValue * 1.2d, 0d);
        }

        const double margin = 0.1d;
        return new TrafficScale(
            maxValue + range * margin,
            Math.Max(0d, minValue - range * margin));
    }

    // 速率使用二进制单位、最多三位有效数字、最多两位小数，并以 KB 为最低单位。
    internal static string FormatRate(double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond))
        {
            return "NaN";
        }

        var units = new[] { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        var exponent = bytesPerSecond < 1d
            ? 0
            : Math.Min(
                (int)Math.Floor(Math.Log2(bytesPerSecond) / 10d),
                units.Length - 1);
        exponent = Math.Max(exponent, 1);
        var scaled = bytesPerSecond / Math.Pow(1024d, exponent);
        var value = scaled >= 1000d
            ? scaled.ToString("0", CultureInfo.CurrentCulture)
            : FormatThreeSignificantDigits(scaled, zeroAsInteger: true);
        return $"{value}\u2009{units[Math.Max(0, exponent)]}";
    }

    private static string FormatThreeSignificantDigits(
        double value,
        bool zeroAsInteger = false)
    {
        if (value == 0)
        {
            return zeroAsInteger ? "0" : "0.00";
        }

        var digitsBeforeDecimal = (int)Math.Floor(Math.Log10(Math.Abs(value))) + 1;
        var decimalPlaces = Math.Min(2, Math.Max(0, 3 - digitsBeforeDecimal));
        var roundedValue = Math.Round(
            value,
            decimalPlaces,
            MidpointRounding.AwayFromZero);
        if (zeroAsInteger && roundedValue == 0d)
        {
            return "0";
        }

        return roundedValue.ToString(
            $"F{decimalPlaces}",
            CultureInfo.CurrentCulture);
    }

    private FormattedText CreateFormattedText(
        string text,
        double fontSize,
        Brush brush,
        TextAlignment alignment,
        bool isBold = false)
    {
        var key = (text, fontSize, GetColor(brush, 1), alignment, isBold);
        if (_textCache.TryGetValue(key, out var cached)) return cached;
        var family = (System.Windows.Media.FontFamily)FindResource("AppTextFont");
        _regularTypeface ??= new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        _boldTypeface ??= new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            isBold ? _boldTypeface : _regularTypeface,
            fontSize,
            brush,
            1d)
        {
            TextAlignment = alignment
        };
        if (_textCache.Count >= 512) _textCache.Clear();
        _textCache.Add(key, formatted);
        return formatted;
    }

    private void DrawText(
        DrawingContext drawingContext,
        string text,
        Point anchor,
        double fontSize,
        Brush brush,
        TextAlignment alignment,
        bool isBold = false)
    {
        var formattedText = CreateFormattedText(text, fontSize, brush, TextAlignment.Left, isBold);
        var drawPoint = alignment switch
        {
            TextAlignment.Right => new Point(anchor.X - formattedText.Width, anchor.Y),
            TextAlignment.Center => new Point(anchor.X - formattedText.Width / 2d, anchor.Y),
            _ => anchor
        };
        drawingContext.DrawText(formattedText, drawPoint);
    }

    private Brush GetTooltipBackgroundBrush()
    {
        var gridColor = GetColor(GridBrush, 1);
        return gridColor.R < 160
            ? CreateBrush(0xEE, 0x2E, 0x2E, 0x2E)
            : CreateBrush(0xEE, 0xFF, 0xFF, 0xFF);
    }

    private static Brush WithOpacity(Brush brush, double opacity)
    {
        if (brush is not SolidColorBrush solidBrush)
        {
            return brush;
        }

        var color = solidBrush.Color;
        color.A = (byte)Math.Clamp(color.A * opacity, 0, 255);
        return CreateBrush(color.A, color.R, color.G, color.B);
    }

    private static Color GetColor(Brush brush, double opacity)
    {
        if (brush is not SolidColorBrush solidBrush)
        {
            return Colors.Transparent;
        }

        var color = solidBrush.Color;
        color.A = (byte)Math.Clamp(color.A * opacity, 0, 255);
        return color;
    }

    private static Brush CreateBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private readonly record struct TrafficScale(double TopValue, double BottomValue);
}
