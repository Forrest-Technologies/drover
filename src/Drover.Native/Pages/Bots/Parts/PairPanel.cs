namespace Drover.Native.Pages;

/// <summary>
/// Settings side by side in columns of the same width, as many as fit at the column's least width, one under
/// another when there is room for only one: the web's grid of auto-fit columns (dv-editor-pair).
/// </summary>
public sealed class PairPanel : Panel
{
    #region Private Methods

    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, width) = Columns(availableSize.Width);
        var height = 0d;

        foreach (var row in Rows(columns))
        {
            var tallest = 0d;
            foreach (var child in row)
            {
                child.Measure(new(width, double.PositiveInfinity));
                tallest = Math.Max(tallest, child.DesiredSize.Height);
            }

            height += tallest + (height > 0 ? Gap : 0);
        }

        return new(double.IsInfinity(availableSize.Width) ? (columns * width) + ((columns - 1) * Gap) : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, width) = Columns(finalSize.Width);
        var y = 0d;

        foreach (var row in Rows(columns))
        {
            var tallest = row.Max(child => child.DesiredSize.Height);
            var x = 0d;
            foreach (var child in row)
            {
                child.Arrange(new(x, y, width, tallest));
                x += width + Gap;
            }

            y += tallest + Gap;
        }

        return finalSize;
    }

    private static double Gap => Tokens.Number("DvSpace4");

    private (int Columns, double Width) Columns(double available)
    {
        var least = Tokens.Number("DvColumnMin");
        var shown = Children.Count(child => child.Visibility is Visibility.Visible);
        if (double.IsInfinity(available) || shown == 0)
        {
            return (Math.Max(shown, 1), least);
        }

        // Columns no setting fills fold away, so the settings there are share the width.
        var columns = Math.Clamp((int)Math.Floor((available + Gap) / (least + Gap)), 1, shown);
        return (columns, Math.Max(0, (available - ((columns - 1) * Gap)) / columns));
    }

    private IEnumerable<List<UIElement>> Rows(int columns) =>
        Children.Where(child => child.Visibility is Visibility.Visible).Chunk(columns).Select(row => row.ToList());

    #endregion
}
