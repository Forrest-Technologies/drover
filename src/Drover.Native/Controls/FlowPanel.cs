namespace Drover.Native.Controls;

/// <summary>
/// Lays its children out in a row and wraps to the next row when the width runs out, with one gap between items
/// and between rows. A child marked <c>FlowPanel.FullRow="True"</c> takes a row of its own.
/// </summary>
public sealed class FlowPanel : Panel
{
    #region Properties

    public static readonly DependencyProperty GapProperty = Dp.Register<FlowPanel, double>(nameof(Gap), 0, panel => panel.InvalidateMeasure());

    public static readonly DependencyProperty FullRowProperty =
        DependencyProperty.RegisterAttached("FullRow", typeof(bool), typeof(FlowPanel), new PropertyMetadata(false));

    /// <summary>The space between items and between rows: a spacing token.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    #endregion

    #region Public Methods

    public static bool GetFullRow(DependencyObject element) => (bool)element.GetValue(FullRowProperty);

    public static void SetFullRow(DependencyObject element, bool value) => element.SetValue(FullRowProperty, value);

    #endregion

    #region Private Methods

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new(availableSize.Width, double.PositiveInfinity));
        }

        return Place(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Place(finalSize.Width, arrange: true);
        return finalSize;
    }

    private Size Place(double width, bool arrange)
    {
        var (x, y, rowHeight, widest) = (0d, 0d, 0d, 0d);

        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            var full = GetFullRow(child);
            var itemWidth = full && !double.IsInfinity(width) ? width : Math.Min(size.Width, double.IsInfinity(width) ? size.Width : width);

            if (x > 0 && (full || x + itemWidth > width))
            {
                (x, y, rowHeight) = (0, y + rowHeight + Gap, 0);
            }

            if (arrange)
            {
                child.Arrange(new(x, y, itemWidth, size.Height));
            }

            rowHeight = Math.Max(rowHeight, size.Height);
            widest = Math.Max(widest, x + itemWidth);
            x += itemWidth + Gap;

            if (full)
            {
                (x, y, rowHeight) = (0, y + rowHeight + Gap, 0);
            }
        }

        return new(double.IsInfinity(width) ? widest : Math.Min(widest, width), y + rowHeight - (rowHeight == 0 && y > 0 ? Gap : 0));
    }

    #endregion
}
