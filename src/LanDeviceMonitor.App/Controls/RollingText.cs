using Control = System.Windows.Controls.Control;
using Size = System.Windows.Size;
using System.Globalization;
using Point = System.Windows.Point;
using System.Windows.Automation.Peers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LanDeviceMonitor.Controls;

public sealed class RollingText : Control
{
    private string _oldText = "";
    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        "Progress", typeof(double), typeof(RollingText), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(RollingText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnTextChanged));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (RollingText)d;
        control._oldText = e.OldValue as string ?? "";
        System.Windows.Automation.AutomationProperties.SetName(control, e.NewValue as string ?? "");
        if (!control.IsLoaded || control._oldText.Length == 0 || !SystemParameters.ClientAreaAnimation) return;
        control.BeginAnimation(ProgressProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    private FormattedText Format(string value) => new(value, CultureInfo.CurrentUICulture, FlowDirection,
        new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
    protected override Size MeasureOverride(Size available)
    {
        var text = Format(Text ?? "");
        return new Size(text.WidthIncludingTrailingWhitespace, text.Height);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    protected override void OnRender(DrawingContext dc)
    {
        var text = Text ?? "";
        var progress = (double)GetValue(ProgressProperty);
        FormattedText Format(string value) => new(value, CultureInfo.CurrentUICulture, FlowDirection,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        var x = 0d;
        for (var i = 0; i < text.Length; i++)
        {
            var glyph = Format(text[i].ToString());
            var oldIndex = i + _oldText.Length - text.Length;
            var old = oldIndex >= 0 && oldIndex < _oldText.Length ? _oldText[oldIndex] : ' ';
            if (progress < 1 && char.IsDigit(text[i]) && old != text[i])
            {
                var height = glyph.Height;
                dc.DrawText(Format(old.ToString()), new Point(x, progress * height));
                dc.DrawText(glyph, new Point(x, (progress - 1) * height));
            }
            else dc.DrawText(glyph, new Point(x, 0));
            x += glyph.WidthIncludingTrailingWhitespace;
        }
        dc.Pop();
    }
}
