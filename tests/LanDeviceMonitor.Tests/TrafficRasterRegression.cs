using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Models;
using Point = System.Windows.Point;

internal static class TrafficRasterRegression
{
    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var origin = DateTimeOffset.UnixEpoch;
                foreach (var range in new[] { 60d, 300, 600 })
                foreach (var upload in new[] { false, true })
                {
                    var samples = Enumerable.Range(0, 660).Select(i => new TrafficSample(origin.AddSeconds(i),
                        (long)(20000 + 18000 * Math.Sin(i * .07)), (long)(70000 + 65000 * Math.Cos(i * .037)))
                        { StartsNewSegment = i == 130 || i == 200 }).ToArray();
                    var transform = new MatrixTransform(new Matrix(500 / range, 0, 0, 1.5, -25 * 500 / range, 200));
                    var series = new TrafficSeriesDrawing(upload, transform);
                    series.SetProjection(500 / range, 1.5); series.SetSamples(samples, origin, 0, 0);
                    series.VisibleBounds(25, 25 + range, 1, 0);
                    var optimized = new DrawingVisual();
                    using (var dc = optimized.RenderOpen())
                    {
                        dc.PushClip(new RectangleGeometry(new Rect(0, 0, 500, 240)));
                        series.Draw(dc, Brushes.CornflowerBlue, 0, 210, 1, 0); dc.Pop();
                    }
                    var segments = Field<EChartsCurve.Segment[]>(series, "_currentSegments");
                    var points = samples.Select(s => new Point((s.Timestamp - origin).TotalSeconds,
                        -(upload ? s.UploadBytesPerSecond : s.DownloadBytesPerSecond) / 1024d)).ToArray();
                    var line = TrafficChart.BuildSampleGeometry(points, 0, false, segments).Clone(); line.Transform = transform.Clone();
                    var area = TrafficChart.BuildSampleGeometry(points, 0, true, segments).Clone(); area.Transform = transform.Clone();
                    var reference = new DrawingVisual();
                    using (var dc = reference.RenderOpen())
                    {
                        dc.PushClip(new RectangleGeometry(new Rect(0, 0, 500, 240)));
                        dc.DrawGeometry(Field<LinearGradientBrush>(series, "_fill"), null, area);
                        dc.DrawGeometry(null, Field<Pen>(series, "_pen"), line); dc.Pop();
                    }
                    foreach (var dpi in new[] { 1d, 1.25, 1.5, 2 })
                    {
                        var before = Capture(reference, dpi); var after = Capture(optimized, dpi);
                        var delta = before.Zip(after, (a, b) => Math.Abs(a - b)).ToArray();
                        Console.WriteLine($"Curve raster range={range}, upload={upload}, DPI={dpi}: max={delta.Max()}, mean={delta.Average():F5}");
                        // Equivalent transform factorizations can round a few
                        // antialiased edge samples differently at fractional DPI.
                        if (delta.Max() > 16 || delta.Average() > .001 || delta.Count(value => value > 2) > 16)
                            throw new InvalidOperationException("Visible-only curve rendering changed the displayed curve or gradient.");
                    }
                    var geometry = Field<GeometryGroup>(series, "_line").Children.Single();
                    var geometryMatrix = Field<MatrixTransform>(series, "_geometryTransform").Matrix;
                    transform.Matrix = transform.Matrix with { OffsetX = transform.Matrix.OffsetX - 1 };
                    using (var dc = new DrawingVisual().RenderOpen()) series.Draw(dc, Brushes.CornflowerBlue, 0, 210, 2, 0);
                    if (!ReferenceEquals(geometry, Field<GeometryGroup>(series, "_line").Children.Single()) ||
                        geometryMatrix != Field<MatrixTransform>(series, "_geometryTransform").Matrix)
                        throw new InvalidOperationException("Time scrolling invalidated retained curve geometry.");
                }
                Console.WriteLine("Traffic raster PASS: visible pixels match complete history, including gaps, both directions, 1/5/10 minutes and four DPI scales.");
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is null) return 0; Console.Error.WriteLine(failure); return 1;
    }

    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static byte[] Capture(Visual visual, double dpi)
    {
        var bitmap = new RenderTargetBitmap((int)(500 * dpi), (int)(240 * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual); var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
    }
}
