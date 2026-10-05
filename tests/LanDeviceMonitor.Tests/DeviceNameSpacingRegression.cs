using System.Windows;
using System.Windows.Documents;
using LanDeviceMonitor.Controls;

internal static class DeviceNameSpacingRegression
{
    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var (name, gaps) in new[] { ("测试设备3", 1), ("华为Mate30", 1), ("中文+English", 2),
                    ("Redmi Note 11T Pro+", 1), ("华为 Mate 30", 1), ("iPhone 13 Pro Max", 0), ("台灯", 0), ("é+灯", 2), ("A×B2", 2), ("设备<abc>", 3), ("设备🙂English", 2) })
                {
                    var display = new DeviceNameTextBlock { NameText = name };
                    foreach (var size in new[] { 14d, 26d, 16d })
                    {
                        display.FontSize = size;
                        var runs = display.Inlines.OfType<Run>().ToArray();
                        if (runs.Count(r => r.Text == " " && Math.Abs(r.FontSize - size / 5) < .001) != gaps)
                            throw new Exception($"Wrong mixed-name gap: {name}, size={size}");
                        if (display.NameText != name || string.Concat(runs.Select(r => r.Text)).Replace(" ", "") != name.Replace(" ", ""))
                            throw new Exception("Stored name or glyphs changed.");
                        if (runs.Any(r => r.Text != " " && r.FontSize != size)) throw new Exception("Device-name glyph size changed.");
                        display.Measure(new Size(600, double.PositiveInfinity));
                        if (display.DesiredSize.Width <= 0) throw new Exception("Mixed device name did not render.");
                    }
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) { Console.Error.WriteLine(failure); return 1; }
        Console.WriteLine("Device-name spacing PASS: mixed-script/symbol/number boundaries, exact one-fifth spaces, preserved English spaces and stored names, font-size changes.");
        return 0;
    }
}
