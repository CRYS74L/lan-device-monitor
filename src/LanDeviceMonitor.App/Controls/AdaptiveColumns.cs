using System.Windows;
using Size = System.Windows.Size;
using System.Windows.Controls;

namespace LanDeviceMonitor.Controls;

// Reflows complete information blocks without shrinking their text.
public sealed class AdaptiveColumns : System.Windows.Controls.Panel
{
    public int Columns { get; set; } = 2;
    public double MinimumColumnWidth { get; set; } = 220;
    public double Gap { get; set; } = 12;
    public bool Justify { get; set; }
    private readonly List<double> _widths = [];
    private int _columns;
    private readonly List<double> _heights = [];
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? MinimumColumnWidth * Columns : available.Width;
        _columns = Math.Clamp((int)((width + Gap) / (MinimumColumnWidth + Gap)), 1, Columns);
        if(Justify)
        {
            _widths.Clear();
            foreach(UIElement child in InternalChildren)
            { child.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));_widths.Add(Math.Min(width,child.DesiredSize.Width)); }
            _columns=Columns;
            while(_columns>1 && Enumerable.Range(0,(_widths.Count+_columns-1)/_columns)
                .Any(row=>_widths.Skip(row*_columns).Take(_columns).Sum()+Gap*(_columns-1)>width)) _columns/=2;
        }
        // Four related metrics should become two balanced pairs, never a 3+1 row.
        if (Columns == 4 && _columns == 3) _columns = 2;
        var cellWidth = Math.Max(0, (width - Gap * (_columns - 1)) / _columns);
        _heights.Clear();
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Measure(new Size(Justify && _columns>1 ? _widths[i] : cellWidth, double.PositiveInfinity));
            if (i % _columns == 0) _heights.Add(0);
            _heights[^1] = Math.Max(_heights[^1], InternalChildren[i].DesiredSize.Height);
        }
        return new Size(width, _heights.Sum() + Math.Max(0, _heights.Count - 1) * Gap);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var cellWidth = Math.Max(0, (finalSize.Width - Gap * (_columns - 1)) / _columns);
        var y = 0d;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var row = i / _columns;
            if (i > 0 && i % _columns == 0) y += _heights[row - 1] + Gap;
            var rowWidths=Justify ? _widths.Skip(row*_columns).Take(_columns).ToArray() : [];
            var gap=Justify && rowWidths.Length>1 ? (finalSize.Width-rowWidths.Sum())/(rowWidths.Length-1) : Gap;
            var x=Justify && _columns>1 ? rowWidths.Take(i%_columns).Sum()+(i%_columns)*gap : (i%_columns)*(cellWidth+Gap);
            InternalChildren[i].Arrange(new Rect(x,y,Justify && _columns>1 ? _widths[i] : cellWidth,_heights[row]));
        }
        return finalSize;
    }
}
