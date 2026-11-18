namespace Drover.Native.Pages;

/// <summary>
/// Items of equal width side by side while each keeps at least the narrowest column, and one above the other when the
/// width runs out: the name and the value of a header, a client id and its secret.
/// </summary>
public sealed class PairPanel : Panel
{
    #region Properties

    public static readonly DependencyProperty GapProperty = Dp.Register<PairPanel, double>(nameof(Gap), 0, panel => panel.InvalidateMeasure());

    /// <summary>The space between items, across and down: a spacing token.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    #endregion

    #region Private Methods

    protected override Size MeasureOverride(Size availableSize) => Place(availableSize.Width, arrange: false);

    protected override Size ArrangeOverride(Size finalSize)
    {
        Place(finalSize.Width, arrange: true);
        return finalSize;
    }

    private Size Place(double width, bool arrange)
    {
        var items = Children.Where(child => child.Visibility is Visibility.Visible).ToList();
        if (items.Count == 0)
        {
            return new(0, 0);
        }

        var narrowest = Tokens.Number("DvColumnMin");
        var limit = double.IsInfinity(width) ? narrowest * items.Count : width;
        var columns = Math.Clamp((int)Math.Floor((limit + Gap) / (narrowest + Gap)), 1, items.Count);
        var each = (limit - (Gap * (columns - 1))) / columns;
        var (y, rowHeight) = (0d, 0d);

        for (var index = 0; index < items.Count; index++)
        {
            var column = index % columns;
            if (column == 0 && index > 0)
            {
                y += rowHeight + Gap;
                rowHeight = 0;
            }

            items[index].Measure(new(each, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, items[index].DesiredSize.Height);

            if (arrange)
            {
                items[index].Arrange(new((each + Gap) * column, y, each, items[index].DesiredSize.Height));
            }
        }

        return new(limit, y + rowHeight);
    }

    #endregion
}
