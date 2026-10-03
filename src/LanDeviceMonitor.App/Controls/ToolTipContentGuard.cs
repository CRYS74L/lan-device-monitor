using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ToolTip=System.Windows.Controls.ToolTip;

namespace LanDeviceMonitor.Controls;

/// <summary>Empty hints must not create a detached popup at the desktop origin.</summary>
public static class ToolTipContentGuard
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached(
        "Enabled",typeof(bool),typeof(ToolTipContentGuard),new PropertyMetadata(false,Attach));
    public static bool GetEnabled(DependencyObject target)=>(bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target,bool value)=>target.SetValue(EnabledProperty,value);
    private static void Attach(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        if(target is not ToolTip tip)return;
        tip.Opened-=Opened;
        if(args.NewValue is true)tip.Opened+=Opened;
    }
    private static void Opened(object sender,RoutedEventArgs args)
    {
        var tip=(ToolTip)sender;
        BindingOperations.GetBindingExpression(tip,ContentControl.ContentProperty)?.UpdateTarget();
        if(tip.Content is null || tip.Content is string text && string.IsNullOrWhiteSpace(text))
            tip.SetCurrentValue(ToolTip.IsOpenProperty,false);
    }
}
