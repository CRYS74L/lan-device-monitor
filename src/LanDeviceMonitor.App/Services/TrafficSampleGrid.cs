using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

// Presentation-only sampling clock. Keep raw arrivals in the history buffer;
// neither reported rates nor byte-delta calculations use this projected time.
internal static class TrafficSampleGrid
{
    internal static IReadOnlyList<TrafficSample> Project(IReadOnlyList<TrafficSample> samples)
    {
        if (samples.Count == 0) return [];
        var result = new List<TrafficSample>(samples.Count);
        var start = 0;
        var period = samples.Select(s => s.IntervalSeconds).FirstOrDefault(p => p is > 0);
        period ??= Estimate(samples);
        for (var i = 1; i <= samples.Count; i++)
        {
            var next = i < samples.Count ? samples[i].IntervalSeconds : null;
            if (i < samples.Count && (next is not > 0 || Math.Abs(next.Value - period.Value) <= Math.Max(.1, period.Value * .04))) continue;
            AppendEpoch(start, i, period.Value);
            start = i;
            if (next is > 0) period = next;
        }
        return result;

        void AppendEpoch(int first, int end, double seconds)
        {
            var ticks = Math.Max(1L, (long)Math.Round(seconds * TimeSpan.TicksPerSecond));
            var slots = new long[end - first];
            var origin = (samples[first].ReceivedAt ?? samples[first].Timestamp).UtcTicks;
            for (var i = first + 1; i < end; i++)
            {
                var gap = ((samples[i].ReceivedAt ?? samples[i].Timestamp) -
                    (samples[i - 1].ReceivedAt ?? samples[i - 1].Timestamp)).Ticks;
                slots[i - first] = slots[i - first - 1] + Math.Max(1, (long)Math.Round(gap / (double)ticks));
                // Fit the grid to observed arrivals without placing a measured
                // point later than it was received. Normal jitter changes phase,
                // never the spacing between neighboring sampling slots.
                origin = Math.Min(origin, (samples[i].ReceivedAt ?? samples[i].Timestamp).UtcTicks - slots[i - first] * ticks);
            }
            if (result.Count > 0 && origin <= result[^1].Timestamp.UtcTicks)
                origin += ((result[^1].Timestamp.UtcTicks - origin) / ticks + 1) * ticks;
            for (var i = first; i < end; i++)
                result.Add(samples[i] with {
                    Timestamp = new DateTimeOffset(origin + slots[i - first] * ticks, TimeSpan.Zero).ToOffset(samples[i].Timestamp.Offset),
                    ReceivedAt = samples[i].ReceivedAt ?? samples[i].Timestamp,
                    IntervalSeconds = seconds,
                    StartsNewSegment = samples[i].StartsNewSegment || first > 0 && i == first ||
                        i > first && slots[i - first] - slots[i - first - 1] > 1
                });
        }
    }

    private static double Estimate(IReadOnlyList<TrafficSample> samples)
    {
        var gaps = samples.Zip(samples.Skip(1), (a, b) =>
            ((b.ReceivedAt ?? b.Timestamp) - (a.ReceivedAt ?? a.Timestamp)).TotalSeconds).Where(g => g > 0).Take(12).Order().ToArray();
        if (gaps.Length == 0) return 1; // One point has no spacing yet.
        var median = gaps[gaps.Length / 2];
        var close = gaps.Where(g => Math.Abs(g - median) <= Math.Max(.2, median * .3)).ToArray();
        var value = close.Average();
        var rounded = Math.Round(value);
        return Math.Max(.001, rounded > 0 && Math.Abs(value - rounded) <= .15 ? rounded : Math.Round(value, 3));
    }
}
