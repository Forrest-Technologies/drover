namespace Drover.Native.Chat;

/// <summary>
/// What a reply used, in a small popover: the model that wrote it and the whole of what it used, under the title
/// "Usage for this reply". It opens at once and says it is adding up while the server reads; a read that failed says
/// so with a way to try again. It takes the focus when it opens, closes on its close button, Escape, or a press
/// outside it, and puts the focus back on what opened it. A reply's own action and the status line under the composer
/// both open this.
/// </summary>
internal sealed class UsagePopover
{
    #region Private Fields

    private readonly StackPanel body = new() { Spacing = Tokens.Number("DvSpace1") };

    private readonly IconButton close = new() { Icon = IconName.Close, Label = CloseLabel, Ink = "DvText3" };

    private readonly StackPanel content = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly Flyout flyout = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };

    private ReplyUsageViewModel? model;

    private Control? opener;

    #endregion

    #region Constructors

    public UsagePopover()
    {
        var title = Styles.Text(ReplyUsageViewModel.Label, "DvTextCaption");
        title.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        var head = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(close, 1);
        head.Children.Add(title);
        head.Children.Add(close);
        close.Click += (_, _) => flyout.Hide();

        content.Children.Add(head);
        content.Children.Add(body);
        AutomationProperties.SetName(content, ReplyUsageViewModel.Label);
        flyout.Content = content;
        flyout.Opened += (_, _) => close.TakeFocus();
        flyout.Closed += (_, _) => WhenClosed();
    }

    #endregion

    #region Properties

    /// <summary>What the reply used, and the model that wrote it.</summary>
    public ReplyUsageViewModel? Model
    {
        get => model;
        set
        {
            if (ReferenceEquals(model, value))
            {
                return;
            }

            model?.PropertyChanged -= Changed;
            model = value;
            model?.PropertyChanged += Changed;
            ShowBody();
        }
    }

    /// <summary>True while the popover is open, so what holds its opener keeps it in view.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Raised when the popover has closed.</summary>
    public event Action? Closed;

    #endregion

    #region Public Methods

    /// <summary>Opens the popover at the control that asked for it, and reads what the reply used if need be.</summary>
    public async Task Open(Control from)
    {
        if (model is null)
        {
            return;
        }

        var presenter = new Style(typeof(FlyoutPresenter));
        presenter.Setters.Add(new Setter(Control.BackgroundProperty, Tokens.Brush(from, "DvBg2")));
        presenter.Setters.Add(new Setter(Control.BorderBrushProperty, Tokens.Brush(from, "DvLine")));
        presenter.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(Tokens.Number("DvBorder"))));
        var corners = new CornerRadius(Tokens.Number("DvRadiusCard"));
        presenter.Setters.Add(new Setter(Control.CornerRadiusProperty, corners));
        presenter.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(Tokens.Number("DvSpace3"))));
        presenter.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
        presenter.Setters.Add(new Setter(Control.IsTabStopProperty, false));
        flyout.FlyoutPresenterStyle = presenter;
        content.RequestedTheme = from.ActualTheme;
        (opener, IsOpen) = (from, true);
        flyout.ShowAt(from);

        if (!model.Open)
        {
            await model.Toggle();
        }
    }

    #endregion

    #region Private Methods

    // What a reply used can arrive from the server's answer, off the screen's own turn.
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (content.DispatcherQueue.HasThreadAccess)
        {
            ShowBody();
        }
        else
        {
            content.DispatcherQueue.TryEnqueue(ShowBody);
        }
    }

    // Closed by its button, Escape, or a press outside it. What was read stays. The focus goes back to what opened it.
    private void WhenClosed()
    {
        IsOpen = false;
        model?.Open = false;
        Closed?.Invoke();

        if (opener is { IsLoaded: true, Visibility: Visibility.Visible } from)
        {
            from.Focus(FocusState.Programmatic);
        }

        opener = null;
    }

    private void ShowBody()
    {
        body.Children.Clear();

        if (model is null || !model.Open)
        {
            return;
        }

        if (model.WrittenBy is { } writtenBy)
        {
            body.Children.Add(Line(writtenBy, AutomationLiveSetting.Off));
        }

        if (model.Problem is { } problem)
        {
            body.Children.Add(Line(problem, AutomationLiveSetting.Assertive));
            var again = new Button { Content = "Try again", HorizontalAlignment = HorizontalAlignment.Left };
            again.Command = model.ReadCommand;
            body.Children.Add(again);
        }
        else if (model.IsAdding)
        {
            body.Children.Add(Line(ReplyUsageViewModel.Adding, AutomationLiveSetting.Polite));
        }
        else
        {
            foreach (var shown in model.Lines)
            {
                body.Children.Add(Line(shown, AutomationLiveSetting.Off));
            }
        }
    }

    private TextBlock Line(string text, AutomationLiveSetting live)
    {
        var shown = Styles.Text(text, "DvTextCaption");
        shown.Foreground = Tokens.Brush(content, "DvText2");
        shown.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetLiveSetting(shown, live);
        return shown;
    }

    #endregion

    #region Fixed Words

    public const string CloseLabel = "Close";

    #endregion
}
