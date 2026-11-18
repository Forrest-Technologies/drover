namespace Drover.Native.Pages;

/// <summary>
/// A row that wraps and spreads: items go left to right with one gap between them, and a row that runs out of width
/// continues below. In a row, the item marked <c>SpreadPanel.Grows</c> takes the width the others leave, and with no
/// such item the width left over goes between the items, the first at the start and the last at the end. It is how a
/// name and its badge, or a title and its button, share a line on a wide screen and stack on a narrow one.
/// </summary>
public sealed class SpreadPanel : Panel
{
    #region Properties

    public static readonly DependencyProperty GapProperty = Dp.Register<SpreadPanel, double>(nameof(Gap), 0, panel => panel.InvalidateMeasure());

    public static readonly DependencyProperty RowGapProperty = Dp.Register<SpreadPanel, double>(nameof(RowGap), 0, panel => panel.InvalidateMeasure());

    public static readonly DependencyProperty AlignProperty =
        Dp.Register<SpreadPanel, VerticalAlignment>(nameof(Align), VerticalAlignment.Top, panel => panel.InvalidateArrange());

    public static readonly DependencyProperty GrowsProperty =
        DependencyProperty.RegisterAttached("Grows", typeof(bool), typeof(SpreadPanel), new PropertyMetadata(false));

    /// <summary>The space between items in a row: a spacing token.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>The space between rows: a spacing token.</summary>
    public double RowGap
    {
        get => (double)GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    /// <summary>Where items of different heights sit in their row: at the top, or in the middle.</summary>
    public VerticalAlignment Align
    {
        get => (VerticalAlignment)GetValue(AlignProperty);
        set => SetValue(AlignProperty, value);
    }

    #endregion

    #region Public Methods

    public static bool GetGrows(DependencyObject element) => (bool)element.GetValue(GrowsProperty);

    public static void SetGrows(DependencyObject element, bool value) => element.SetValue(GrowsProperty, value);

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
        var limit = double.IsInfinity(width) ? double.PositiveInfinity : width;
        var rows = Rows(limit);
        var (y, widest) = (0d, 0d);

        foreach (var row in rows)
        {
            var widths = Widths(row, limit);
            var heights = new List<double>();

            for (var index = 0; index < row.Count; index++)
            {
                row[index].Measure(new(widths[index], double.PositiveInfinity));
                heights.Add(row[index].DesiredSize.Height);
            }

            var rowHeight = heights.Max();
            var extra = Spread(row, widths, limit);
            var x = 0d;

            for (var index = 0; index < row.Count; index++)
            {
                var top = Align is VerticalAlignment.Center ? (rowHeight - heights[index]) / 2 : 0;
                if (arrange)
                {
                    row[index].Arrange(new(x, y + top, widths[index], heights[index]));
                }

                x += widths[index] + Gap + extra;
            }

            widest = Math.Max(widest, widths.Sum() + (Gap * (row.Count - 1)));
            y += rowHeight + RowGap;
        }

        return new(double.IsInfinity(limit) ? widest : limit, Math.Max(0, y - (rows.Count > 0 ? RowGap : 0)));
    }

    // The width a child asks for in a row of its own, and for one that grows, the least it is given.
    private double Basis(UIElement child, double limit)
    {
        child.Measure(new(limit, double.PositiveInfinity));
        var asked = child.DesiredSize.Width;
        return GetGrows(child) ? Math.Min(Tokens.Number("DvColumnMin"), Math.Min(asked, limit)) : Math.Min(asked, limit);
    }

    private List<List<UIElement>> Rows(double limit)
    {
        List<List<UIElement>> rows = [];
        List<UIElement> row = [];
        var used = 0d;

        foreach (var child in Children.Where(child => child.Visibility is Visibility.Visible))
        {
            var basis = Basis(child, limit);
            if (row.Count > 0 && used + Gap + basis > limit)
            {
                rows.Add(row);
                (row, used) = ([], 0d);
            }

            used += (row.Count > 0 ? Gap : 0) + basis;
            row.Add(child);
        }

        if (row.Count > 0)
        {
            rows.Add(row);
        }

        return rows;
    }

    // A growing item takes what the others leave.
    private List<double> Widths(List<UIElement> row, double limit)
    {
        var widths = row.Select(child => Basis(child, limit)).ToList();
        var growing = row.FindIndex(GetGrows);

        if (growing >= 0 && !double.IsInfinity(limit))
        {
            widths[growing] = Math.Max(widths[growing], limit - (widths.Sum() - widths[growing]) - (Gap * (row.Count - 1)));
        }

        return widths;
    }

    // What is left over in a row with nothing that grows goes between the items.
    private double Spread(List<UIElement> row, List<double> widths, double limit) =>
        row.Count < 2 || row.Any(GetGrows) || double.IsInfinity(limit)
            ? 0
            : Math.Max(0, limit - widths.Sum() - (Gap * (row.Count - 1))) / (row.Count - 1);

    #endregion
}
