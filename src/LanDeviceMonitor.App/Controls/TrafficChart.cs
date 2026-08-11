using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LanDeviceMonitor.Models;
using LanDeviceMonitor.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace LanDeviceMonitor.Controls;

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
                FrameworkPropertyMetadataOptions.AffectsRender));

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
                TrafficHistoryBuffer.WindowMinutes,
                FrameworkPropertyMetadataOptions.AffectsRender));

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

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 100 || height < 80)
        {
            return;
        }

        var left = 44d;
        var right = Math.Max(left + 1, width - 14d);
        var top = 22d;
        var bottom = Math.Max(top + 1, height - 31d);
        var allSamples = Samples ?? Array.Empty<TrafficSample>();
        var end = allSamples.Count > 0
            ? allSamples[^1].Timestamp
            : DateTimeOffset.Now;
        var rangeMinutes = RangeMinutes is 1 or 5 or 10 ? RangeMinutes : 10;
        var start = end - TimeSpan.FromMinutes(rangeMinutes);
        var samples = allSamples
            .Where(sample => sample.Timestamp >= start)
            .ToList();
        var topValue = CalculateTopValue(samples);

        DrawGrid(drawingContext, left, right, top, bottom);
        DrawAxisLabels(drawingContext, left, top, bottom, topValue);
        DrawTimeAxis(drawingContext, left, right, bottom, start, end, rangeMinutes);
        DrawHeader(drawingContext, right);
        DrawFooter(drawingContext, left, bottom, samples.Count, end);

        if (samples.Count == 0)
        {
            return;
        }

        DrawTrafficSeries(
            drawingContext,
            samples,
            start,
            end,
            left,
            right,
            top,
            bottom,
            topValue,
            UploadBrush,
            sample => sample.UploadBytesPerSecond);
        DrawTrafficSeries(
            drawingContext,
            samples,
            start,
            end,
            left,
            right,
            top,
            bottom,
            topValue,
            DownloadBrush,
            sample => sample.DownloadBytesPerSecond);
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
        double topValue)
    {
        var axisBrush = WithOpacity(LabelBrush, 0.9);
        DrawText(
            drawingContext,
            FormatRate(topValue),
            new Point(left - 7, top - 7),
            10,
            axisBrush,
            System.Windows.TextAlignment.Right);
        DrawText(
            drawingContext,
            FormatRate(topValue / 2d),
            new Point(left - 7, (top + bottom) / 2d - 7),
            10,
            axisBrush,
            System.Windows.TextAlignment.Right);
        DrawText(
            drawingContext,
            "0",
            new Point(left - 7, bottom - 7),
            10,
            axisBrush,
            System.Windows.TextAlignment.Right);
    }

    private void DrawTimeAxis(
        DrawingContext drawingContext,
        double left,
        double right,
        double bottom,
        DateTimeOffset start,
        DateTimeOffset end,
        int rangeMinutes)
    {
        var span = end - start;
        var labelBrush = WithOpacity(LabelBrush, 0.72);
        for (var index = 0; index <= 5; index++)
        {
            var x = left + (right - left) * index / 5d;
            var timestamp = start + TimeSpan.FromTicks(span.Ticks * index / 5);
            var alignment = index == 0
                ? System.Windows.TextAlignment.Left
                : index == 5
                    ? System.Windows.TextAlignment.Right
                    : System.Windows.TextAlignment.Center;
            var format = rangeMinutes == 1 ? "mm:ss" : "HH:mm";
            DrawText(
                drawingContext,
                timestamp.ToString(format, CultureInfo.CurrentCulture),
                new Point(x, bottom + 8),
                10,
                labelBrush,
                alignment);
        }
    }

    private void DrawHeader(
        DrawingContext drawingContext,
        double right)
    {
        DrawText(
            drawingContext,
            "上传",
            new Point(right - 6, 6),
            12,
            UploadBrush,
            System.Windows.TextAlignment.Right,
            isBold: true);
        DrawText(
            drawingContext,
            "下载",
            new Point(right - 6, 25),
            12,
            DownloadBrush,
            System.Windows.TextAlignment.Right,
            isBold: true);
    }

    private void DrawFooter(
        DrawingContext drawingContext,
        double left,
        double bottom,
        int sampleCount,
        DateTimeOffset end)
    {
        DrawText(
            drawingContext,
            $"采样点：{sampleCount} · 更新：{end:HH:mm:ss}",
            new Point(left, bottom + 22),
            9,
            WithOpacity(LabelBrush, 0.62),
            System.Windows.TextAlignment.Left);
    }

    private void DrawTrafficSeries(
        DrawingContext drawingContext,
        IReadOnlyList<TrafficSample> samples,
        DateTimeOffset start,
        DateTimeOffset end,
        double left,
        double right,
        double top,
        double bottom,
        double topValue,
        Brush brush,
        Func<TrafficSample, long> valueSelector)
    {
        var points = samples
            .Select(sample => new Point(
                GetX(sample.Timestamp, start, end, left, right),
                GetY(valueSelector(sample), topValue, top, bottom)))
            .ToList();
        if (points.Count == 0)
        {
            return;
        }

        if (points.Count > 1)
        {
            var area = BuildQuadraticGeometry(points, bottom, closeToBaseline: true);
            var fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            fill.GradientStops.Add(new GradientStop(GetColor(brush, 0.12), 0));
            fill.GradientStops.Add(new GradientStop(GetColor(brush, 0), 1));
            fill.Freeze();
            drawingContext.DrawGeometry(fill, null, area);

            var line = BuildQuadraticGeometry(points, bottom, closeToBaseline: false);
            var pen = new Pen(brush, 2.4)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            drawingContext.DrawGeometry(null, pen, line);
        }
        else
        {
            drawingContext.DrawEllipse(brush, null, points[0], 2.5, 2.5);
        }
    }

    private static StreamGeometry BuildQuadraticGeometry(
        IReadOnlyList<Point> points,
        double baseline,
        bool closeToBaseline)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], closeToBaseline, isClosed: false);
            for (var index = 1; index < points.Count; index++)
            {
                var current = points[index];
                var next = points[Math.Min(index + 1, points.Count - 1)];
                var midpoint = new Point(
                    (current.X + next.X) / 2d,
                    (current.Y + next.Y) / 2d);
                context.QuadraticBezierTo(
                    current,
                    midpoint,
                    isStroked: true,
                    isSmoothJoin: true);
            }

            if (closeToBaseline)
            {
                context.LineTo(
                    new Point(points[^1].X, baseline),
                    isStroked: false,
                    isSmoothJoin: false);
                context.LineTo(
                    new Point(points[0].X, baseline),
                    isStroked: false,
                    isSmoothJoin: false);
                context.Close();
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static double GetX(
        DateTimeOffset timestamp,
        DateTimeOffset start,
        DateTimeOffset end,
        double left,
        double right)
    {
        var totalSeconds = Math.Max(1d, (end - start).TotalSeconds);
        var ratio = Math.Clamp((timestamp - start).TotalSeconds / totalSeconds, 0d, 1d);
        return left + (right - left) * ratio;
    }

    private static double GetY(
        long value,
        double topValue,
        double top,
        double bottom)
    {
        var ratio = Math.Clamp(Math.Max(0, value) / topValue, 0d, 1d);
        return bottom - (bottom - top) * ratio;
    }

    private static double CalculateTopValue(IReadOnlyList<TrafficSample> samples)
    {
        var maxValue = samples.Count == 0
            ? 0d
            : samples.Max(sample =>
                Math.Max(sample.UploadBytesPerSecond, sample.DownloadBytesPerSecond));
        if (maxValue <= 0)
        {
            return 1024;
        }

        var target = maxValue * 1.1;
        var unit = Math.Pow(1024, Math.Floor(Math.Log(target, 1024)));
        var normalized = target / unit;
        var step = normalized <= 1
            ? 1
            : normalized <= 2
                ? 2
                : normalized <= 5
                    ? 5
                    : 10;
        return step * unit;
    }

    private static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024)
        {
            return $"{bytesPerSecond:0} B";
        }

        if (bytesPerSecond < 1024 * 1024)
        {
            return $"{bytesPerSecond / 1024d:0.#} KB";
        }

        if (bytesPerSecond < 1024L * 1024 * 1024)
        {
            return $"{bytesPerSecond / 1024d / 1024d:0.#} MB";
        }

        return $"{bytesPerSecond / 1024d / 1024d / 1024d:0.#} GB";
    }

    private static void DrawText(
        DrawingContext drawingContext,
        string text,
        Point anchor,
        double fontSize,
        Brush brush,
        System.Windows.TextAlignment alignment,
        bool isBold = false)
    {
        var typeface = new Typeface(
            new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
            FontStyles.Normal,
            isBold ? FontWeights.SemiBold : FontWeights.Normal,
            FontStretches.Normal);
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            1d)
        {
            TextAlignment = System.Windows.TextAlignment.Left
        };
        var drawPoint = alignment switch
        {
            System.Windows.TextAlignment.Right =>
                new Point(anchor.X - formattedText.Width, anchor.Y),
            System.Windows.TextAlignment.Center =>
                new Point(anchor.X - formattedText.Width / 2d, anchor.Y),
            _ => anchor
        };
        drawingContext.DrawText(formattedText, drawPoint);
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

}
