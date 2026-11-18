namespace Drover.Native.Pages;

/// <summary>
/// Keeps an element in reach at the bottom of the scrolling view while its place is further down, and lets it go
/// back to its place when that comes into view: the web's sticky position, for the editor's save bar.
/// </summary>
public static class StickyBottom
{
    #region Public Methods

    /// <summary>Holds <paramref name="element"/> a gap of <paramref name="gap"/> above the bottom of the view it scrolls in.</summary>
    public static void Hold(FrameworkElement element, string gap)
    {
        var lift = new TranslateTransform();
        element.RenderTransform = lift;
        ScrollViewer? view = null;

        void Place()
        {
            if (view is null || element.Visibility is Visibility.Collapsed || element.ActualHeight == 0)
            {
                lift.Y = 0;
                return;
            }

            // Where the element's bottom would be in the view without the lift, and where the view lets it be.
            var top = element.TransformToVisual(view).TransformPoint(new(0, 0)).Y - lift.Y;
            var reach = view.ViewportHeight - Tokens.Number(gap);
            var parentTop = element.Parent is FrameworkElement parent ? parent.TransformToVisual(view).TransformPoint(new(0, 0)).Y : 0;
            lift.Y = Math.Min(0, Math.Max(parentTop - top, reach - (top + element.ActualHeight)));
        }

        element.Loaded += (_, _) =>
        {
            view = Ancestor(element);
            if (view is not null)
            {
                view.ViewChanged += (_, _) => Place();
                view.SizeChanged += (_, _) => Place();
            }

            Place();
        };
        element.SizeChanged += (_, _) => Place();
        element.LayoutUpdated += (_, _) => Place();
    }

    #endregion

    #region Private Methods

    private static ScrollViewer? Ancestor(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ScrollViewer view)
            {
                return view;
            }
        }

        return null;
    }

    #endregion
}
