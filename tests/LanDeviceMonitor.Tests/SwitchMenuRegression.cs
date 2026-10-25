using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Controls;
using LanDeviceMonitor.Services;

internal static class SwitchMenuRegression
{
    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => {
            App? app = null;
            Window? host = null;
            try {
                app = TestAppFactory.Create(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var toggle = new SwitchControl();
                host = new Window { Content = toggle, Width = 200, Height = 120, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false };
                host.Show(); host.UpdateLayout(); Pump(100);
                foreach (var mode in new[] { ThemeService.LightMode, ThemeService.DarkMode }) {
                    host.Show(); Pump(80); ThemeService.Apply(mode); toggle.IsChecked = false; Pump(240);
                    var accent = (Border)toggle.Template.FindName("PART_AccentTrack",toggle);
                    var knob = (Border)toggle.Template.FindName("PART_Knob",toggle);
                    var move = (TranslateTransform)knob.RenderTransform;
                    Require(accent.Opacity == 0 && move.X == 0,"off state");
                    Require(toggle.ClickMode == ClickMode.Press,"switch activation must happen on press");
                    var pressedKey = (DependencyPropertyKey)typeof(ButtonBase).GetField("IsPressedPropertyKey",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
                    toggle.SetValue(pressedKey,true); Pump(40);
                    Require(knob.Opacity == 1 && toggle.Opacity == 1 && move.X == 0,"pressed state changed switch appearance");
                    toggle.SetValue(pressedKey,false);

                    toggle.IsChecked = true;
                    Console.WriteLine($"Switch start: accent={accent.Opacity:F3}, knob={move.X/20:F3}, animation={SystemParameters.ClientAreaAnimation}, loaded={toggle.IsLoaded}");
                    if (SystemParameters.ClientAreaAnimation) Require(accent.Opacity<.01 && move.X<.1,"theme color jumped before switch movement");
                    Pump(60);
                    if (SystemParameters.ClientAreaAnimation) {
                        Require(accent.Opacity > .05 && accent.Opacity < 1,"on fade has no intermediate frame");
                        Require(Math.Abs(accent.Opacity - move.X/20) < .01,"accent and knob progress differ");
                    }
                    Console.WriteLine($"Switch {mode} at 60ms: accent={accent.Opacity:F3}, knob={move.X/20:F3}");
                    var progress = accent.Opacity;
                    toggle.IsChecked = false;
                    Require(Math.Abs(accent.Opacity-progress)<.01,"interrupted fade snapped");
                    Pump(60);
                    Console.WriteLine($"Switch reverse at 60ms: accent={accent.Opacity:F3}, knob={move.X/20:F3}, prior={progress:F3}");
                    if (SystemParameters.ClientAreaAnimation) Require(accent.Opacity > 0 && accent.Opacity < progress && Math.Abs(accent.Opacity-move.X/20)<.01,"off fade differs from knob");
                    Pump(200); Require(accent.Opacity==0 && move.X==0,"off transition did not settle");
                    toggle.IsChecked=true; Pump(240); Require(accent.Opacity==1 && move.X==20,"on transition did not settle");
                    Capture(toggle,$"switch-on-{mode}.png");
                    host.Hide(); Pump(80);
                    var opened=0; var exited=0;
                    using var commands = new TrayCommandMenu(()=>opened++,()=>exited++);
                    commands.Show(new Point(650,550)); Pump(220);
                    var menu=commands.CurrentMenu ?? throw new Exception("menu missing");
                    Require(menu.IsOpen && menu.Items.Count==2 && menu.Placement==PlacementMode.Custom && !menu.StaysOpen,"menu lifecycle or placement changed");
                    var surface=Surface(menu);
                    Require(surface.CornerRadius.TopLeft==19 && surface.BorderThickness==new Thickness(1.5) && surface.Padding==new Thickness(3),"ReTray plate geometry changed");
                    var items=menu.Items.Cast<MenuItem>().ToArray();
                    Require(items.All(i=>i.ActualHeight==29 && i.FontSize==12.75 && i.Margin==new Thickness(0) && StudioControlMotion.GetEnabled(i)),"ReTray rows changed");
                    Require(items.Select(i=>(string)i.Header).SequenceEqual(new[]{"打开主界面","退出应用"}),"commands changed");
                    Require(Keyboard.FocusedElement is MenuItem,"hidden owner menu did not acquire keyboard focus");
                    var down = new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(menu),Environment.TickCount,Key.Down) { RoutedEvent=Keyboard.KeyDownEvent };
                    ((UIElement)Keyboard.FocusedElement).RaiseEvent(down); Pump(80);
                    Require(ReferenceEquals(Keyboard.FocusedElement,items[1]),"Down must move focus to Exit");
                    var up = new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(menu),Environment.TickCount,Key.Up) { RoutedEvent=Keyboard.KeyDownEvent };
                    ((UIElement)Keyboard.FocusedElement).RaiseEvent(up); Pump(80);
                    Require(ReferenceEquals(Keyboard.FocusedElement,items[0]),"Up must move focus to Open");
                    var anchor=(HwndSource)PresentationSource.FromVisual(menu.PlacementTarget);
                    Capture(menu,$"tray-menu-{mode}.png");
                    var esc = new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(menu),Environment.TickCount,Key.Escape) { RoutedEvent=Keyboard.KeyDownEvent };
                    ((UIElement)Keyboard.FocusedElement).RaiseEvent(esc); Pump(400);
                    Console.WriteLine($"Menu Esc: open={menu.IsOpen}, current={commands.CurrentMenu is not null}, anchorDisposed={anchor.IsDisposed}, handled={esc.Handled}");
                    Require(!menu.IsOpen && commands.CurrentMenu is null && anchor.IsDisposed,"Esc did not close/release anchor");
                    commands.Show(new Point(650,550)); Pump(100);
                    commands.Show(new Point(650,550)); Pump(400);
                    Require(commands.CurrentMenu is null,"second request must toggle menu closed");
                    commands.Show(new Point(650,550)); Pump(100);
                    ((MenuItem)commands.CurrentMenu!.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(400);
                    Require(opened==1 && exited==0 && commands.CurrentMenu is null,"open action and dismissal");
                    commands.Show(new Point(650,550)); Pump(100);
                    ((MenuItem)commands.CurrentMenu!.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(400);
                    Require(opened==1 && exited==1 && commands.CurrentMenu is null,"exit action and dismissal");
                    Console.WriteLine($"ReTray menu {mode} PASS: {surface.ActualWidth:F3} x {surface.ActualHeight:F3} DIP; keyboard, toggle, commands and native anchor release.");
                }
                foreach (var scale in new[]{1d,1.25,1.5,2d}) {
                    var pos=TrayMenuPlacement.Candidate(new Size(170*scale,132*scale),new Thickness(12,6,12,22),new DpiScale(scale,scale));
                    Require(Math.Abs(pos.Point.X+12*scale)<1e-6 && Math.Abs(pos.Point.Y+110*scale)<1e-6 && pos.PrimaryAxis==PopupPrimaryAxis.None,"DPI placement");
                }
            } catch(Exception e) { failure=e; }
            finally { host?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if(!thread.Join(TimeSpan.FromSeconds(45))) {Console.Error.WriteLine("Switch/menu test timed out");return 1;}
        if(failure is not null) {Console.Error.WriteLine(failure);return 1;}
        Console.WriteLine("Switch/menu regression PASS."); return 0;
    }
    private static Border Surface(ContextMenu menu) {
        var content=(ContentControl)VisualTreeHelper.GetChild(menu,0);content.ApplyTemplate();
        return (Border)content.Template.FindName("DropdownSurface",content);
    }
    private static void Capture(FrameworkElement element,string name) {
        var folder=Path.Combine(Environment.CurrentDirectory,".tools","switch-menu-v0.1.14");Directory.CreateDirectory(folder);
        var bmp=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth*2),(int)Math.Ceiling(element.ActualHeight*2),192,192,PixelFormats.Pbgra32);
        bmp.Render(element);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(Path.Combine(folder,name));png.Save(file);
    }
    private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    private static void Pump(int ms) {var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
}
