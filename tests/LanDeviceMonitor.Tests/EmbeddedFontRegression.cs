using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;

internal static class EmbeddedFontRegression
{
    public static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            LanDeviceMonitor.App? app = null;
            try
            {
                app = TestAppFactory.Create();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var family = (FontFamily)app.FindResource("AppTextFont");
                var faces = new[]
                {
                    (FontWeights.Light, "Light", "NotoSansSC-VF.ttf", "763146584CF0710223441356B4395E279021B0806C196614377A7A0174AE074A"),
                    (FontWeights.Normal, "Regular", "NotoSansSC-VF.ttf", "763146584CF0710223441356B4395E279021B0806C196614377A7A0174AE074A"),
                    (FontWeights.Medium, "Medium", "NotoSansSC-VF.ttf", "763146584CF0710223441356B4395E279021B0806C196614377A7A0174AE074A"),
                    (FontWeights.SemiBold, "SemiBold", "NotoSansSC-SemiBold.ttf", "ACC3276790DF63B1134E4694CA21CE875238E4826004C0BBADDAA582D65866D5")
                };
                foreach (var (weight, style, file, hash) in faces)
                {
                    var face = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
                    if (!face.TryGetGlyphTypeface(out var glyph) || glyph.FontUri.Scheme != "pack" ||
                        !glyph.FontUri.ToString().EndsWith($"/{file}", StringComparison.OrdinalIgnoreCase) ||
                        glyph.Weight != weight || face.IsBoldSimulated || face.IsObliqueSimulated)
                        throw new Exception($"内嵌简体字体未正确匹配 {style}: {glyph?.FontUri}");
                    using var stream = Application.GetResourceStream(glyph.FontUri).Stream;
                    if (Convert.ToHexString(SHA256.HashData(stream)) != hash)
                        throw new Exception($"内嵌字体与指定源文件不同: {style}");
                    foreach (var point in "局域网设备助手连接设置上下行历史0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz℃％：/-.()●…")
                        if (!glyph.CharacterToGlyphMap.ContainsKey(point))
                            throw new Exception($"{style} 缺少界面字符 U+{(int)point:X4}");
                    CheckThinSpace(face);
                    Console.WriteLine($"PASS embedded Noto Sans SC {style}: exact source hash, native weight, UI character coverage");
                }
                var icons = new Typeface((FontFamily)app.FindResource("AppIconFont"), FontStyles.Normal,
                    FontWeights.Normal, FontStretches.Normal);
                if (!icons.TryGetGlyphTypeface(out var icon) || icon.FontUri.Scheme != "pack" ||
                    !icon.FontUri.ToString().EndsWith("/SegoeIcons.ttf", StringComparison.OrdinalIgnoreCase) ||
                    !icon.CharacterToGlyphMap.ContainsKey(0xE713))
                    throw new Exception("独立内嵌图标字体加载失败");
                Console.WriteLine("PASS embedded icon font remains available");
            }
            catch (Exception exception) { failure = exception; }
            finally { app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is null) return 0;
        Console.Error.WriteLine(failure);
        return 1;
    }

    private static void CheckThinSpace(Typeface face)
    {
        var text = new FormattedText("1\u2009KB", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, face, 20, Brushes.Black, 1);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawText(text, new Point());
        var runs = GlyphRuns(drawing.Drawing).ToArray();
        if (runs.Any(run => run.GlyphTypeface.FontUri.Scheme != "pack" || run.GlyphIndices.Contains((ushort)0)))
            throw new Exception("数字与单位排版仍依赖系统字体或缺字");
        var spacing = runs.Where(run => run.GlyphTypeface.FontUri.ToString()
            .EndsWith("/AppTextSpacing.ttf", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (spacing.Length != 1 || Math.Abs(spacing[0].AdvanceWidths.Sum() - 4) > 0.001)
            throw new Exception("窄空格未使用内嵌的五分之一字宽间距");
    }

    private static IEnumerable<GlyphRun> GlyphRuns(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph.GlyphRun;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var run in GlyphRuns(child)) yield return run;
    }
}
