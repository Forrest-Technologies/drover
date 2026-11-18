namespace Drover.Native.Pages;

/// <summary>
/// A row of a list in the editor, the web's wrapping flex row: the first child takes the room that is left and
/// never less than a column's least width, the second sits at the row's end at its own width, and moves under the
/// first when they do not fit side by side. Any further child takes a row of its own below.
/// </summary>
public sealed class WrapRow : Panel
{
    #region Properties

    private static double Gap => Tokens.Number("DvSpace3");

    #endregion

    #region Private Methods

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? Tokens.Number("DvWorkWidth") : availableSize.Width;
        return Place(width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Place(finalSize.Width, arrange: true);
        return finalSize;
    }

    private Size Place(double width, bool arrange)
    {
        List<UIElement> shown = [.. Children.Where(child => child.Visibility is Visibility.Visible)];
        var y = 0d;

        if (shown is [var lead, ..])
        {
            var end = shown.Count > 1 ? shown[1] : null;
            end?.Measure(new(width, double.PositiveInfinity));
            var endWidth = end?.DesiredSize.Width ?? 0;
            var beside = end is not null && width - endWidth - Gap >= Tokens.Number("DvColumnMin");
            var leadWidth = beside ? width - endWidth - Gap : width;
            lead.Measure(new(leadWidth, double.PositiveInfinity));
            var lineHeight = beside ? Math.Max(lead.DesiredSize.Height, endWidth > 0 ? end!.DesiredSize.Height : 0) : lead.DesiredSize.Height;

            if (arrange)
            {
                lead.Arrange(new(0, beside ? (lineHeight - lead.DesiredSize.Height) / 2 : 0, leadWidth, lead.DesiredSize.Height));
            }

            y = lineHeight;

            if (end is not null)
            {
                if (beside && arrange)
                {
                    end.Arrange(new(width - endWidth, (lineHeight - end.DesiredSize.Height) / 2, endWidth, end.DesiredSize.Height));
                }
                else if (!beside)
                {
                    y += Gap;
                    if (arrange)
                    {
                        end.Arrange(new(0, y, Math.Min(endWidth, width), end.DesiredSize.Height));
                    }

                    y += end.DesiredSize.Height;
                }
            }

            foreach (var rest in shown.Skip(2))
            {
                rest.Measure(new(width, double.PositiveInfinity));
                y += Gap;
                if (arrange)
                {
                    rest.Arrange(new(0, y, width, rest.DesiredSize.Height));
                }

                y += rest.DesiredSize.Height;
            }
        }

        return new(width, y);
    }

    #endregion
}
