using System.Windows;
using MediaBrush = System.Windows.Media.Brush;

namespace LanDeviceMonitor.Controls;

public sealed class DeviceTypeIcon : System.Windows.Controls.Control
{
    static DeviceTypeIcon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(DeviceTypeIcon),
            new FrameworkPropertyMetadata(typeof(DeviceTypeIcon)));
    }

    public static readonly DependencyProperty DeviceTypeProperty =
        DependencyProperty.Register(
            nameof(DeviceType),
            typeof(string),
            typeof(DeviceTypeIcon),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(
            nameof(Glyph),
            typeof(string),
            typeof(DeviceTypeIcon),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconBrushProperty =
        DependencyProperty.Register(
            nameof(IconBrush),
            typeof(MediaBrush),
            typeof(DeviceTypeIcon),
            new PropertyMetadata(System.Windows.Media.Brushes.DodgerBlue));

    public string DeviceType
    {
        get => (string)GetValue(DeviceTypeProperty);
        set => SetValue(DeviceTypeProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public MediaBrush IconBrush
    {
        get => (MediaBrush)GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }
}
