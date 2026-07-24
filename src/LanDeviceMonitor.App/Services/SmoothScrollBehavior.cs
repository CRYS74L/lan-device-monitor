using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LanDeviceMonitor.Services;

internal static class SmoothScrollBehavior
{
    private const double WheelDistance = 82d;
    private const double DurationMilliseconds = 240d;
    private static readonly ConditionalWeakTable<ScrollViewer, ScrollAnimation> Animations = new();

    public static bool TryHandle(DependencyObject source, MouseWheelEventArgs e)
    {
        var viewer = FindVisualParent<ScrollViewer>(source);
        if (viewer is null || viewer.ScrollableHeight <= 0)
        {
            return false;
        }

        var animation = Animations.GetValue(viewer, target => new ScrollAnimation(target));
        return animation.ScrollBy(-Math.Sign(e.Delta) * WheelDistance);
    }

    internal static double EaseOutCubic(double progress)
    {
        var clamped = Math.Clamp(progress, 0d, 1d);
        return 1d - Math.Pow(1d - clamped, 3d);
    }

    private static T? FindVisualParent<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed class ScrollAnimation
    {
        private readonly ScrollViewer _viewer;
        private readonly Stopwatch _stopwatch = new();
        private double _startOffset;
        private double _targetOffset;
        private bool _isRendering;

        public ScrollAnimation(ScrollViewer viewer)
        {
            _viewer = viewer;
            _targetOffset = viewer.VerticalOffset;
        }

        public bool ScrollBy(double delta)
        {
            var current = _viewer.VerticalOffset;
            var basis = _isRendering ? _targetOffset : current;
            var target = Math.Clamp(basis + delta, 0d, _viewer.ScrollableHeight);
            if (Math.Abs(target - current) < 0.1d)
            {
                return false;
            }

            if (!SystemParameters.ClientAreaAnimation)
            {
                _viewer.ScrollToVerticalOffset(target);
                _targetOffset = target;
                return true;
            }

            _startOffset = current;
            _targetOffset = target;
            _stopwatch.Restart();
            if (!_isRendering)
            {
                _isRendering = true;
                CompositionTarget.Rendering += CompositionTarget_Rendering;
            }

            return true;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (!_viewer.IsVisible)
            {
                Stop();
                return;
            }

            var progress = _stopwatch.Elapsed.TotalMilliseconds / DurationMilliseconds;
            var eased = EaseOutCubic(progress);
            _viewer.ScrollToVerticalOffset(
                _startOffset + ((_targetOffset - _startOffset) * eased));

            if (progress >= 1d)
            {
                _viewer.ScrollToVerticalOffset(_targetOffset);
                Stop();
            }
        }

        private void Stop()
        {
            if (!_isRendering)
            {
                return;
            }

            _isRendering = false;
            _stopwatch.Stop();
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
        }
    }
}
