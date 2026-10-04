using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace LanDeviceMonitor.Controls;

public sealed class HistoryDateTextBlock : TextBlock
{
    public static readonly DependencyProperty DateTextProperty = DependencyProperty.Register(
        nameof(DateText), typeof(string), typeof(HistoryDateTextBlock),
        new PropertyMetadata(string.Empty, (owner, _) => ((HistoryDateTextBlock)owner).UpdateInlines()));

    public string DateText
    {
        get => (string)GetValue(DateTextProperty);
        set => SetValue(DateTextProperty, value);
    }

    static HistoryDateTextBlock()
    {
        FontSizeProperty.OverrideMetadata(typeof(HistoryDateTextBlock),
            new FrameworkPropertyMetadata(FontSizeProperty.GetMetadata(typeof(TextBlock)).DefaultValue,
                (owner, _) => ((HistoryDateTextBlock)owner).UpdateInlines()));
    }

    private void UpdateInlines()
    {
        Inlines.Clear();
        var parts = (DateText ?? string.Empty).Split('\u2009');
        for (var index = 0; index < parts.Length; index++)
        {
            if (index > 0)
                // Scale only the blank glyph, whose advance is proportional
                // to font size. Date glyphs retain the normal face and size.
                Inlines.Add(new Run(" ") { FontSize = FontSize / 5 });
            Inlines.Add(new Run(parts[index]));
        }
    }
}
