namespace Drover.Native.Chat;

/// <summary>
/// A row of chips that fills its width: each chip keeps the room its words need and takes an even share of what
/// is left over. When the chips do not fit one row they wrap, and each row is filled the same way. Without
/// <see cref="Fills"/> the chips keep their own widths and the row ends where they end.
/// </summary>
internal sealed class SpreadPanel : Panel
{
    #region Properties

    public static readonly DependencyProperty GapProperty = Dp.Register<SpreadPanel, double>(nameof(Gap), 0, panel => panel.InvalidateMeasure());

    public static readonly DependencyProperty FillsProperty = Dp.Register<SpreadPanel, bool>(nameof(Fills), false, panel => panel.InvalidateMeasure());

    /// <summary>The space between chips and between rows: a spacing token.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>True where the chips share the whole width of each row.</summary>
    public bool Fills
    {
        get => (bool)GetValue(FillsProperty);
        set => SetValue(FillsProperty, value);
    }

    #endregion

    #region Private Methods

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        }

        var rows = Rows(availableSize.Width);
        var height = rows.Sum(row => row.Max(child => child.DesiredSize.Height)) + (Gap * Math.Max(0, rows.Count - 1));
        var widest = rows.Count == 0 ? 0 : rows.Max(Needed);
        return new(Fills && !double.IsInfinity(availableSize.Width) ? availableSize.Width : Math.Min(widest, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var top = 0d;

        foreach (var row in Rows(finalSize.Width))
        {
            var share = Fills ? Math.Max(0, finalSize.Width - Needed(row)) / row.Count : 0;
            var height = row.Max(child => child.DesiredSize.Height);
            var left = 0d;

            foreach (var child in row)
            {
                var width = child.DesiredSize.Width + share;
                child.Arrange(new(left, top, width, height));
                left += width + Gap;
            }

            top += height + Gap;
        }

        return finalSize;
    }

    private double Needed(List<UIElement> row) => row.Sum(child => child.DesiredSize.Width) + (Gap * (row.Count - 1));

    // The chips that are shown, row by row: a chip that does not fit beside the ones before it starts the next row.
    private List<List<UIElement>> Rows(double width)
    {
        List<List<UIElement>> rows = [];
        var used = 0d;

        foreach (var child in Children.Where(child => child.Visibility is Visibility.Visible))
        {
            var needs = child.DesiredSize.Width;
            if (rows.Count == 0 || used + Gap + needs > width)
            {
                rows.Add([]);
                used = needs;
            }
            else
            {
                used += Gap + needs;
            }

            rows[^1].Add(child);
        }

        return rows;
    }

    #endregion
}
