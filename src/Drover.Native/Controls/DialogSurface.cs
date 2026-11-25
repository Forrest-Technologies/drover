namespace Drover.Native.Controls;

/// <summary>
/// The modal layer a <see cref="Dialog"/> and a <see cref="Sheet"/> share. It covers the window with the flat dim,
/// holds a panel with a title, a Close button, a body, and actions, keeps Tab inside the panel, closes on Escape,
/// and puts the focus back where it was when it closes. While it is open, a device's Back closes it first.
/// </summary>
internal sealed partial class DialogSurface
{
    #region Private Fields

    private readonly FrameworkElement owner;

    private readonly bool atEdge;

    private readonly Popup popup = new() { IsLightDismissEnabled = false };

    private readonly Grid layer = new();

    private readonly Grid safeArea = new();

    private readonly Border scrim = new();

    private readonly Border panel = new();

    private readonly TextBlock title = Styles.Text(null, "DvTextH3");

    private readonly PlainButton close = new() { Content = "Close", Line = "DvText3" };

    private readonly Border header = new();

    private readonly ContentPresenter body = new();

    private readonly Border footer = new();

    private readonly ContentPresenter actions = new() { HorizontalAlignment = HorizontalAlignment.Right };

    private readonly TranslateTransform slide = new();

    private Control? opener;

    private Navigator? closesFor;

    #endregion

    #region Constructors

    public DialogSurface(FrameworkElement owner, bool atEdge)
    {
        (this.owner, this.atEdge) = (owner, atEdge);
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        title.VerticalAlignment = VerticalAlignment.Center;
        close.Padding = new(Tokens.Number("DvSpace3"), 0, Tokens.Number("DvSpace3"), 0);
        close.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        close.Click += (_, _) =>
            Close(close.FocusState is FocusState.Pointer ? FocusState.Pointer : FocusState.Keyboard);

        var top = new Grid { ColumnSpacing = Tokens.Number("DvSpace3") };
        top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(close, 1);
        top.Children.Add(title);
        top.Children.Add(close);
        header.Child = top;
        header.Padding = new(Tokens.Number("DvSpace4"));
        header.BorderThickness = new(0, 0, 0, Tokens.Number("DvBorder"));

        var scroll = new ScrollViewer
        {
            Content = body,
            Padding = new(Tokens.Number("DvSpace4")),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            IsTabStop = false,
        };
        footer.Child = actions;
        footer.Padding = new(Tokens.Number("DvSpace4"));
        footer.BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0);

        var column = new Grid();
        column.RowDefinitions.Add(new() { Height = GridLength.Auto });
        column.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        column.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetRow(scroll, 1);
        Grid.SetRow(footer, 2);
        column.Children.Add(header);
        column.Children.Add(scroll);
        column.Children.Add(footer);

        panel.Child = column;
        panel.RenderTransform = slide;
        panel.TabFocusNavigation = KeyboardNavigationMode.Cycle;
        panel.KeyDown += (_, key) => CloseOnEscape(key);
        layer.Children.Add(scrim);
        layer.Children.Add(safeArea);
        safeArea.Children.Add(panel);
#if ANDROID
        // Popups are siblings of the shell, so they also need to respect the window's visible bounds.
        SafeArea.Attach(safeArea);
        safeArea.RegisterPropertyChangedCallback(Grid.PaddingProperty, (_, _) =>
        {
            if (popup.XamlRoot is { } root)
            {
                Place(root.Size);
            }
        });
#endif
        popup.Child = layer;
    }

    #endregion

    #region Properties

    /// <summary>The app's navigator, told while a surface is open so that a step back closes it first.</summary>
    public static Navigator? Navigator { get; set; }

    /// <summary>Raised when the person closed the surface: Escape, Back, or the Close button.</summary>
    public event Action? Closed;

    /// <summary>
    /// For a surface that takes the place of one already showing: it is simply there, with no fade, so the dim
    /// behind it does not blink.
    /// </summary>
    public bool AtOnce { get; set; }

    /// <summary>Called before closing. Returning false keeps the surface open, for a dialog that asks first.</summary>
    public Func<bool>? Closing { get; set; }

    /// <summary>
    /// How the person closed the surface: with the keyboard, or with a pointer or a touch. The keyboard until then.
    /// </summary>
    public FocusState ClosedWith { get; private set; } = FocusState.Keyboard;

    #endregion

    #region Public Methods

    public void Show(string heading, object? content, object? buttons, FrameworkElement? initial = null)
    {
        if (owner.XamlRoot is not { } root)
        {
            return;
        }

        if (!popup.IsOpen)
        {
            opener = FocusManager.GetFocusedElement(root) as Control;
            ClosedWith = FocusState.Keyboard;
            popup.XamlRoot = root;
            root.Changed += Resize;
        }

        title.Text = heading;
        body.Content = content;
        actions.Content = buttons;
        footer.Visibility = buttons is null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(panel, heading);
        layer.RequestedTheme = owner.ActualTheme;
        Paint();
        Place(root.Size);

        if (!popup.IsOpen)
        {
            closesFor = Navigator;
            closesFor?.AddCloser(CloseFromBack);
            popup.IsOpen = true;
            if (!AtOnce)
            {
                Motion.FadeIn(layer);
                SlideIn(root.Size);
            }

            var focus = initial ?? close;
            focus.Loaded += FocusInitial;
            focus.Focus(FocusState.Keyboard);
        }
    }

    public void Hide()
    {
        if (!popup.IsOpen)
        {
            return;
        }

        popup.IsOpen = false;
        closesFor?.RemoveCloser(CloseFromBack);
        closesFor = null;
        body.Content = null;
        actions.Content = null;
        if (popup.XamlRoot is { } root)
        {
            root.Changed -= Resize;
        }

        opener?.Focus(ClosedWith);
        opener = null;
    }

    #endregion

    #region Private Methods

    private static void FocusInitial(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement focus)
        {
            focus.Loaded -= FocusInitial;
            focus.Focus(FocusState.Keyboard);
        }
    }

    private void Close(FocusState with)
    {
        if (Closing?.Invoke() is false)
        {
            return;
        }

        ClosedWith = with;
        Closed?.Invoke();
    }

    // A step back comes from a button or a swipe of the device, not from the keyboard.
    private void CloseFromBack() => Close(FocusState.Pointer);

    private void CloseOnEscape(KeyRoutedEventArgs key)
    {
        if (key.Key is VirtualKey.Escape)
        {
            key.Handled = true;
            Close(FocusState.Keyboard);
        }
    }

    private void Resize(XamlRoot root, XamlRootChangedEventArgs args) => Place(root.Size);

    private bool ComesFromBelow(double width) => atEdge && width < (double)Styles.Dictionary["DvBreakSheet"];

    private Size SafeSize(Size window)
    {
        var padding = safeArea.Padding;
        return new(Math.Max(0, window.Width - padding.Left - padding.Right),
            Math.Max(0, window.Height - padding.Top - padding.Bottom));
    }

    // A dialog sits in the middle. A sheet takes the trailing edge, and on a narrow window comes up from the bottom.
    private void Place(Size window)
    {
        var (border, card, sheet) = (Tokens.Number("DvBorder"), Tokens.Number("DvRadiusCard"), Tokens.Number("DvRadiusSheet"));
        layer.Width = window.Width;
        layer.Height = window.Height;
        var available = SafeSize(window);

        if (!atEdge)
        {
            panel.Width = Math.Clamp(available.Width - Tokens.Number("DvSpace6"), 0, Tokens.Number("DvDialogWidth"));
            panel.MaxHeight = Math.Max(0, available.Height - Tokens.Number("DvSpace6"));
            panel.Height = double.NaN;
            (panel.HorizontalAlignment, panel.VerticalAlignment) = (HorizontalAlignment.Center, VerticalAlignment.Center);
            (panel.BorderThickness, panel.CornerRadius) = (new(border), new(card));
        }
        else if (ComesFromBelow(available.Width))
        {
            panel.Width = available.Width;
            panel.MaxHeight = Math.Max(0, available.Height - Tokens.Number("DvSpace7"));
            panel.Height = double.NaN;
            (panel.HorizontalAlignment, panel.VerticalAlignment) = (HorizontalAlignment.Stretch, VerticalAlignment.Bottom);
            (panel.BorderThickness, panel.CornerRadius) = (new(0, border, 0, 0), new(sheet, sheet, 0, 0));
        }
        else
        {
            panel.Width = Math.Min(Tokens.Number("DvSheetWidth"), available.Width);
            panel.MaxHeight = panel.Height = available.Height;
            (panel.HorizontalAlignment, panel.VerticalAlignment) = (HorizontalAlignment.Right, VerticalAlignment.Stretch);
            (panel.BorderThickness, panel.CornerRadius) = (new(border, 0, 0, 0), new(sheet, 0, 0, sheet));
        }
    }

    // A sheet arrives from the edge it sits on. With reduced motion it is simply there.
    private void SlideIn(Size window)
    {
        (slide.X, slide.Y) = (0, 0);
        if (!atEdge || Motion.IsReduced)
        {
            return;
        }

        var fromBelow = ComesFromBelow(SafeSize(window).Width);
        var arrive = new DoubleAnimation
        {
            From = fromBelow ? window.Height / 2 : panel.Width,
            To = 0,
            Duration = Motion.Duration("DvDur2"),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(arrive, slide);
        Storyboard.SetTargetProperty(arrive, fromBelow ? nameof(TranslateTransform.Y) : nameof(TranslateTransform.X));
        var storyboard = new Storyboard();
        storyboard.Children.Add(arrive);
        storyboard.Begin();
    }

    private void Paint()
    {
        scrim.Background = Tokens.Brush(layer, "DvScrim");
        panel.Background = Tokens.Brush(layer, "DvBg2");
        panel.BorderBrush = Tokens.Brush(layer, "DvLine");
        header.BorderBrush = Tokens.Brush(layer, "DvLine");
        footer.BorderBrush = Tokens.Brush(layer, "DvLine");
    }

    #endregion
}
