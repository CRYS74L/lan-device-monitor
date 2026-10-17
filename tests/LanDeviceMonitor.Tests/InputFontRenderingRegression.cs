using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Controls;

internal static class InputFontRenderingRegression
{
    public static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            Window? window = null;
            try
            {
                app = TestAppFactory.Create();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var bodyText = new TextBlock { Text = "正文参照" };
                var textBox = new TextBox { Text = "输入文字" };
                var passwordBox = new PasswordBox { Password = "密码" };
                var comboBox = new ComboBox
                {
                    ItemsSource = new[] { "下拉菜单文字" },
                    SelectedIndex = 0
                };
                var content = new StackPanel();
                content.Children.Add(bodyText);
                content.Children.Add(textBox);
                content.Children.Add(passwordBox);
                content.Children.Add(comboBox);

                window = new Window
                {
                    Width = 360,
                    Height = 280,
                    Left = -10000,
                    Top = -10000,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Content = content
                };
                window.Show();
                window.UpdateLayout();

                RequireReferenceRendering(bodyText);
                RequireControlTextRendering(textBox, "输入框胶囊");
                var textPlaceholder = RequireNotNull(
                    textBox.Template.FindName("Placeholder", textBox) as TextBlock,
                    "输入框占位文字未生成");
                RequireControlTextRendering(textPlaceholder, "输入框占位文字");
                RequireClearTypeHint(textPlaceholder, ClearTypeHint.Enabled, "输入框占位文字");
                var textBoxContent = RequireNotNull(
                    textBox.Template.FindName("PART_ContentHost", textBox) as ScrollViewer,
                    "输入框内容区域未生成");
                RequireContentTextRendering(textBoxContent, "输入框文字内容区域");
                RequireClearTypeHint(textBoxContent, ClearTypeHint.Enabled, "输入框文字内容区域");
                var textBoxView = Descendants<DependencyObject>(textBoxContent)
                    .FirstOrDefault(element => element.GetType().Name == "TextBoxView");
                if (textBoxView is not null)
                {
                    RequireContentTextRendering(textBoxView, "输入框文字绘制器");
                }

                RequireControlTextRendering(passwordBox, "密码框胶囊");
                var passwordPlaceholder = RequireNotNull(
                    passwordBox.Template.FindName("Placeholder", passwordBox) as TextBlock,
                    "密码框占位文字未生成");
                RequireControlTextRendering(passwordPlaceholder, "密码框占位文字");
                RequireClearTypeHint(passwordPlaceholder, ClearTypeHint.Enabled, "密码框占位文字");
                var passwordContent = RequireNotNull(
                    passwordBox.Template.FindName("PART_ContentHost", passwordBox) as ScrollViewer,
                    "密码框内容区域未生成");
                RequireContentTextRendering(passwordContent, "密码框文字内容区域");
                RequireClearTypeHint(passwordContent, ClearTypeHint.Enabled, "密码框文字内容区域");

                RequireControlTextRendering(comboBox, "下拉框本体胶囊");
                comboBox.ApplyTemplate();
                var toggle = RequireNotNull(
                    comboBox.Template.FindName("DropDownToggle", comboBox) as ToggleButton,
                    "下拉框胶囊按钮未生成");
                RequireContentTextRendering(toggle, "下拉框胶囊按钮");
                var selectedPresenter = RequireNotNull(
                    Descendants<ContentPresenter>(toggle)
                        .FirstOrDefault(presenter => presenter.Content?.ToString() == "下拉菜单文字"),
                    "下拉框本体文字未生成");
                RequireContentTextRendering(selectedPresenter, "下拉框本体文字");
                RequireClearTypeHint(selectedPresenter, ClearTypeHint.Enabled, "下拉框本体文字");
                var selectedText = Descendants<TextBlock>(toggle)
                    .FirstOrDefault(text => text.Text == "下拉菜单文字");

                comboBox.IsDropDownOpen = true;
                window.UpdateLayout();
                Pump(240);
                var popup = RequireNotNull(
                    comboBox.Template.FindName("PART_Popup", comboBox) as Popup,
                    "下拉菜单未生成");
                var popupRoot = RequireNotNull(popup.Child, "下拉菜单容器未生成");
                var popupSurface = RequireNotNull(
                    popupRoot as RoundedCardBorder ??
                    Descendants<RoundedCardBorder>(popupRoot).FirstOrDefault(),
                    "下拉菜单表面未生成");
                var popupBorders = new List<Border>();
                if (popupRoot is Border popupBorder) popupBorders.Add(popupBorder);
                popupBorders.AddRange(Descendants<Border>(popupRoot));
                var shadowLayer = popupBorders.FirstOrDefault(border => border.Effect is DropShadowEffect);
                if (popupSurface.Effect is not null || shadowLayer is null || ReferenceEquals(shadowLayer, popupSurface))
                {
                    throw new InvalidOperationException("下拉菜单阴影仍套在文字表面上，会把文字一起送入效果合成层。");
                }

                if (selectedText is not null)
                {
                    RequireControlTextRendering(selectedText, "下拉框胶囊文字绘制器");
                    RequireClearTypeHint(selectedText, ClearTypeHint.Enabled, "下拉框胶囊文字绘制器");
                }
                RequireControlTextRendering(popupSurface, "下拉菜单表面");
                RequireClearTypeHint(popupSurface, ClearTypeHint.Auto, "下拉菜单表面");
                var menuItem = RequireNotNull(
                    comboBox.ItemContainerGenerator.ContainerFromIndex(0) as ComboBoxItem,
                    "下拉菜单项目未生成");
                RequireControlTextRendering(menuItem, "下拉菜单项目");
                RequireClearTypeHint(menuItem, ClearTypeHint.Auto, "下拉菜单项目");
                var menuText = RequireNotNull(
                    Descendants<TextBlock>(menuItem)
                        .FirstOrDefault(text => text.Text == "下拉菜单文字"),
                    "下拉菜单文字未生成");
                RequireControlTextRendering(menuText, "下拉菜单文字");
                RequireClearTypeHint(menuText, ClearTypeHint.Auto, "下拉菜单文字");

                Console.WriteLine("输入框与下拉胶囊使用 Auto 抗锯齿；菜单阴影与文字绘制层分离，菜单文字保持 Auto 渲染。");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (window is { IsVisible: true }) window.Close();
                app?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            Console.Error.WriteLine(failure.Message);
            return 1;
        }

        return 0;
    }

    private static void RequireReferenceRendering(DependencyObject body)
    {
        var expected = (
            TextFormattingMode.Ideal,
            TextRenderingMode.Grayscale,
            TextHintingMode.Auto,
            ClearTypeHint.Auto);
        var actual = ReadRendering(body);

        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"正文参考渲染方式变化：期望={expected}，实际={actual}");
        }
    }

    private static void RequireControlTextRendering(DependencyObject element, string name)
    {
        var expected = (
            TextFormattingMode.Display,
            TextRenderingMode.Auto,
            TextHintingMode.Fixed);
        var actual = ReadTextRendering(element);
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"{name}未使用已验证的清晰文字渲染方式：期望={expected}，实际={actual}");
        }
    }

    private static void RequireContentTextRendering(DependencyObject element, string name)
    {
        var expected = (
            TextFormattingMode.Display,
            TextRenderingMode.Auto);
        var actual = (
            TextOptions.GetTextFormattingMode(element),
            TextOptions.GetTextRenderingMode(element));
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"{name}未使用已验证的清晰文字渲染方式：期望={expected}，实际={actual}");
        }
    }

    private static void RequireClearTypeHint(
        DependencyObject element,
        ClearTypeHint expected,
        string name)
    {
        var actual = RenderOptions.GetClearTypeHint(element);
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"{name} ClearType 提示不正确：期望={expected}，实际={actual}");
        }
    }

    private static (
        TextFormattingMode Formatting,
        TextRenderingMode Rendering,
        TextHintingMode Hinting) ReadTextRendering(DependencyObject element) =>
        (
            TextOptions.GetTextFormattingMode(element),
            TextOptions.GetTextRenderingMode(element),
            TextOptions.GetTextHintingMode(element));

    private static (
        TextFormattingMode Formatting,
        TextRenderingMode Rendering,
        TextHintingMode Hinting,
        ClearTypeHint ClearType) ReadRendering(DependencyObject element) =>
        (
            TextOptions.GetTextFormattingMode(element),
            TextOptions.GetTextRenderingMode(element),
            TextOptions.GetTextHintingMode(element),
            RenderOptions.GetClearTypeHint(element));

    private static T RequireNotNull<T>(T? value, string message) where T : class =>
        value ?? throw new InvalidOperationException(message);

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(milliseconds),
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }
}
