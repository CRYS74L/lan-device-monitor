using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using LanDeviceMonitor;

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

                var bodyReference = bodyText;
                RequireSameRendering(bodyReference, textBox, "输入框胶囊");
                RequireSameRendering(
                    bodyReference,
                    RequireNotNull(
                        textBox.Template.FindName("PART_ContentHost", textBox) as ScrollViewer,
                        "输入框内容区域未生成"),
                    "输入框文字内容区域");

                RequireSameRendering(bodyReference, passwordBox, "密码框胶囊");
                RequireSameRendering(
                    bodyReference,
                    RequireNotNull(
                        passwordBox.Template.FindName("PART_ContentHost", passwordBox) as ScrollViewer,
                        "密码框内容区域未生成"),
                    "密码框文字内容区域");

                RequireSameRendering(bodyReference, comboBox, "下拉框本体胶囊");
                comboBox.ApplyTemplate();
                var toggle = RequireNotNull(
                    comboBox.Template.FindName("DropDownToggle", comboBox) as ToggleButton,
                    "下拉框胶囊按钮未生成");
                RequireSameRendering(bodyReference, toggle, "下拉框胶囊按钮");
                RequireSameRendering(
                    bodyReference,
                    RequireNotNull(
                        Descendants<ContentPresenter>(toggle)
                            .FirstOrDefault(presenter => presenter.Content?.ToString() == "下拉菜单文字"),
                        "下拉框本体文字未生成"),
                    "下拉框本体文字");

                comboBox.IsDropDownOpen = true;
                window.UpdateLayout();
                Pump(240);
                var popup = RequireNotNull(
                    comboBox.Template.FindName("PART_Popup", comboBox) as Popup,
                    "下拉菜单未生成");
                var popupSurface = RequireNotNull(popup.Child, "下拉菜单表面未生成");
                RequireSameRendering(bodyReference, popupSurface, "下拉菜单表面");
                var menuItem = RequireNotNull(
                    comboBox.ItemContainerGenerator.ContainerFromIndex(0) as ComboBoxItem,
                    "下拉菜单项目未生成");
                RequireSameRendering(bodyReference, menuItem, "下拉菜单项目");
                RequireSameRendering(
                    bodyReference,
                    RequireNotNull(
                        Descendants<TextBlock>(menuItem)
                            .FirstOrDefault(text => text.Text == "下拉菜单文字"),
                        "下拉菜单文字未生成"),
                    "下拉菜单文字");

                Console.WriteLine("输入框、密码框、下拉框胶囊和下拉菜单均与正文渲染一致。");
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

    private static void RequireSameRendering(
        DependencyObject reference,
        DependencyObject actual,
        string name)
    {
        var expected = (
            TextOptions.GetTextFormattingMode(reference),
            TextOptions.GetTextRenderingMode(reference),
            TextOptions.GetTextHintingMode(reference),
            RenderOptions.GetClearTypeHint(reference));
        var value = (
            TextOptions.GetTextFormattingMode(actual),
            TextOptions.GetTextRenderingMode(actual),
            TextOptions.GetTextHintingMode(actual),
            RenderOptions.GetClearTypeHint(actual));

        if (expected != value)
        {
            throw new InvalidOperationException(
                $"{name}渲染方式与正文不一致：正文={expected}，控件={value}");
        }
    }

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
