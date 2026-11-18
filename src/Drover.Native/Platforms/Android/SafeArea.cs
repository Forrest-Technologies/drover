namespace Drover.Native.Droid;

/// <summary>Keeps a full-window grid's children inside Uno's Android visible bounds.</summary>
internal static class SafeArea
{
    #region Public Methods

    public static void Attach(Grid grid)
    {
        grid.Loaded += (_, _) =>
        {
            if (Application.Current is not App { MainWindow: { } window })
            {
                return;
            }

            var view = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView();
            view.VisibleBoundsChanged += VisibleBoundsChanged;
            window.SizeChanged += SizeChanged;
            grid.Unloaded += Detach;
            ApplyPadding();

            void VisibleBoundsChanged(Windows.UI.ViewManagement.ApplicationView sender, object args) => ApplyPadding();

            void SizeChanged(object sender, WindowSizeChangedEventArgs args) => ApplyPadding();

            void Detach(object sender, RoutedEventArgs args)
            {
                view.VisibleBoundsChanged -= VisibleBoundsChanged;
                window.SizeChanged -= SizeChanged;
                grid.Unloaded -= Detach;
            }

            void ApplyPadding()
            {
                var bounds = window.Bounds;
                var visible = view.VisibleBounds;
                if (visible.Width > 0 && visible.Height > 0)
                {
                    grid.Padding = new(
                        Math.Max(0, visible.Left - bounds.Left),
                        Math.Max(0, visible.Top - bounds.Top),
                        Math.Max(0, bounds.Right - visible.Right),
                        Math.Max(0, bounds.Bottom - visible.Bottom));
                }
            }
        };
    }

    #endregion
}
