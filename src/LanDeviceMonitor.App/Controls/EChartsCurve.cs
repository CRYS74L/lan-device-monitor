// Licensed to the Apache Software Foundation (ASF) under one or more
// contributor license agreements. See ThirdParty/echarts/NOTICE.
// The ASF licenses this work under the Apache License, Version 2.0.
// https://www.apache.org/licenses/LICENSE-2.0
// C# adaptation of Apache ECharts 6.0.0, src/chart/line/poly.ts drawSegment:
// smooth=0.5, smoothMonotone='none', with finite, time-ordered samples.
using Point = System.Windows.Point;

namespace LanDeviceMonitor.Controls;

internal static class EChartsCurve
{
    internal readonly record struct Segment(Point Start, Point First, Point Second, Point End);

    internal static Segment[] Build(IReadOnlyList<Point> points, double scaleX = 1, double scaleY = 1)
    {
        if (points.Count < 2) return [];
        // ECharts receives display coordinates. Translation has no effect on its
        // control points; retain data coordinates after applying the display scale.
        var projected = points.Select(p => new Point(p.X * scaleX, p.Y * scaleY)).ToArray();
        var segments = new List<Segment>(points.Count - 1);
        var previous = projected[0]; var first = previous;
        Point Restore(Point p) => new(p.X / scaleX, p.Y / scaleY);
        for (var i = 1; i < projected.Length; i++)
        {
            var point = projected[i];
            var dx = point.X - previous.X; var dy = point.Y - previous.Y;
            if (dx * dx + dy * dy < .5) continue;
            var second = point; var nextFirst = point;
            if (i + 1 < projected.Length)
            {
                var next = projected[i + 1];
                var vx = next.X - previous.X; var vy = next.Y - previous.Y;
                var lenPrevious = Math.Sqrt(dx * dx + dy * dy);
                var nextDx = next.X - point.X; var nextDy = next.Y - point.Y;
                var lenNext = Math.Sqrt(nextDx * nextDx + nextDy * nextDy);
                var ratioNext = lenNext / (lenNext + lenPrevious);
                second = new(point.X - vx * .5 * (1 - ratioNext), point.Y - vy * .5 * (1 - ratioNext));
                nextFirst = new(point.X + vx * .5 * ratioNext, point.Y + vy * .5 * ratioNext);
                nextFirst = Clamp(nextFirst, point, next);
                vx = nextFirst.X - point.X; vy = nextFirst.Y - point.Y;
                second = new(point.X - vx * lenPrevious / lenNext, point.Y - vy * lenPrevious / lenNext);
                second = Clamp(second, previous, point);
                vx = point.X - second.X; vy = point.Y - second.Y;
                nextFirst = new(point.X + vx * lenNext / lenPrevious, point.Y + vy * lenNext / lenPrevious);
            }
            segments.Add(new(Restore(previous), Restore(first), Restore(second), Restore(point)));
            first = nextFirst; previous = point;
        }
        return segments.ToArray();
    }

    internal static Point Clamp(Point point, Point a, Point b) => new(
        Math.Clamp(point.X, Math.Min(a.X, b.X), Math.Max(a.X, b.X)),
        Math.Clamp(point.Y, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)));
}
