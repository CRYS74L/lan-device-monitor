using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Size = System.Windows.Size;

namespace LanDeviceMonitor.Controls;

/// <summary>Reveals older rows without rebuilding the virtualized history tree.</summary>
public sealed class HistoryDisclosure : Decorator
{
    public static readonly DependencyProperty RecentCountProperty = DependencyProperty.Register(nameof(RecentCount),typeof(int),typeof(HistoryDisclosure),new FrameworkPropertyMetadata(0,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty TotalCountProperty = DependencyProperty.Register(nameof(TotalCount),typeof(int),typeof(HistoryDisclosure),new FrameworkPropertyMetadata(0,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded),typeof(bool),typeof(HistoryDisclosure),new PropertyMetadata(false,ExpandedChanged));
    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register("Progress",typeof(double),typeof(HistoryDisclosure),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public int RecentCount {get=>(int)GetValue(RecentCountProperty);set=>SetValue(RecentCountProperty,value);}
    public int TotalCount {get=>(int)GetValue(TotalCountProperty);set=>SetValue(TotalCountProperty,value);}
    public bool IsExpanded {get=>(bool)GetValue(IsExpandedProperty);set=>SetValue(IsExpandedProperty,value);}
    private double _fullHeight;
    private int _generation;
    public HistoryDisclosure() { ClipToBounds=true; }
    private static void ExpandedChanged(DependencyObject source,DependencyPropertyChangedEventArgs args)
    {
        var host=(HistoryDisclosure)source;
        var current=(double)host.GetValue(ProgressProperty);
        var target=(bool)args.NewValue ? 1d : 0d;
        var generation=++host._generation;
        host.BeginAnimation(ProgressProperty,null);host.SetValue(ProgressProperty,target);
        if(!host.IsLoaded || !SystemParameters.ClientAreaAnimation)return;
        var animation=new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(target==1?420:320))
        {EasingFunction=target==1 ? new QuinticEase {EasingMode=EasingMode.EaseOut} : new CubicEase {EasingMode=EasingMode.EaseInOut}};
        animation.Completed+=(_,_)=>{if(generation==host._generation)host.BeginAnimation(ProgressProperty,null);};
        host.BeginAnimation(ProgressProperty,animation,HandoffBehavior.SnapshotAndReplace);
    }
    public void Reset()
    {
        ++_generation;BeginAnimation(ProgressProperty,null);SetCurrentValue(IsExpandedProperty,false);
        ++_generation;BeginAnimation(ProgressProperty,null);SetValue(ProgressProperty,0d);
    }
    protected override Size MeasureOverride(Size available)
    {
        if(Child is null)return new Size();
        Child.Measure(new Size(available.Width,double.PositiveInfinity));
        _fullHeight=Child.DesiredSize.Height;
        var recent=TotalCount>0 ? _fullHeight*Math.Clamp((double)RecentCount/TotalCount,0,1) : 0;
        return new Size(Child.DesiredSize.Width,recent+(_fullHeight-recent)*(double)GetValue(ProgressProperty));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0,0,finalSize.Width,_fullHeight));
        return finalSize;
    }
}
