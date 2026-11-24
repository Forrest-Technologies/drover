namespace Drover.Native.Chat;

/// <summary>
/// The hint of what wrote a reply and what it used: the model by a short name and, once read, the tokens and the
/// cost. It is a button; pressed, a small popover holds the model's whole name and the whole of what the reply used,
/// and closes on its close button, Escape, or a press outside it. A reply's footer and the line under the composer
/// both show this one control. Where the line is too narrow the model's name is cut short first.
/// </summary>
public sealed class UsageHint : Grid
{
    #region Private Fields

    private readonly PlainButton hint = new()
    {
        Ink = "DvText3",
        HoverInk = "DvText2",
        HoverFill = null,
        Radius = null,
        MinHeight = Targets.Minimum,
        Padding = new(Tokens.Number("DvSpace1"), 0, Tokens.Number("DvSpace1"), 0),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left,
        FontSize = Tokens.Number("DvText12"),
    };

    private readonly Icon modelMark = new() { NameOfIcon = IconName.Model, VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock modelWords = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private readonly TextBlock figures = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };

    private readonly StackPanel body = new() { Spacing = Tokens.Number("DvSpace1") };

    private readonly IconButton close = new() { Icon = IconName.Close, Label = CloseLabel, Ink = "DvText3" };

    private readonly StackPanel popover = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly Flyout flyout = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };

    private ReplyUsageViewModel? model;

    #endregion

    #region Constructors

    public UsageHint()
    {
        // The name takes what the mark and the figures leave, so it is the part that is cut short.
        var hintRow = new Grid { ColumnSpacing = Tokens.Number("DvSpace1") };
        hintRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        hintRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        hintRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        FrameworkElement[] parts = [modelMark, modelWords, figures];
        foreach (var part in parts)
        {
            SetColumn(part, hintRow.Children.Count);
            hintRow.Children.Add(part);
            AutomationProperties.SetAccessibilityView(part, AccessibilityView.Raw);
        }

        hint.Content = hintRow;
        hint.Click += async (_, _) => await Open();

        var title = Styles.Text(ReplyUsageViewModel.Label, "DvTextCaption");
        title.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        var head = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        SetColumn(close, 1);
        head.Children.Add(title);
        head.Children.Add(close);
        close.Click += (_, _) => flyout.Hide();

        popover.Children.Add(head);
        popover.Children.Add(body);
        AutomationProperties.SetName(popover, ReplyUsageViewModel.Label);
        flyout.Content = popover;
        flyout.Opened += (_, _) => close.TakeFocus();
        flyout.Closed += (_, _) => WhenClosed();

        Children.Add(hint);
        this.WhenShownOrRethemed(Show);
    }

    #endregion

    #region Properties

    /// <summary>What the reply used, and the model that wrote it. Without it there is no hint.</summary>
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
            Show();
        }
    }

    /// <summary>True while the popover is open, so what holds the hint keeps it in view.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Raised when the popover has closed.</summary>
    public event Action? Closed;

    #endregion

    #region Private Methods

    // What a reply used can arrive from the server's answer, off the screen's own turn.
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            Show();
        }
        else
        {
            DispatcherQueue.TryEnqueue(Show);
        }
    }

    // The popover opens at once and says it is adding up while the server reads what the reply used.
    private async Task Open()
    {
        if (model is null)
        {
            return;
        }

        var presenter = new Style(typeof(FlyoutPresenter));
        presenter.Setters.Add(new Setter(Control.BackgroundProperty, Tokens.Brush(this, "DvBg2")));
        presenter.Setters.Add(new Setter(Control.BorderBrushProperty, Tokens.Brush(this, "DvLine")));
        presenter.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(Tokens.Number("DvBorder"))));
        presenter.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(Tokens.Number("DvRadiusCard"))));
        presenter.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(Tokens.Number("DvSpace3"))));
        presenter.Setters.Add(new Setter(MinHeightProperty, 0d));
        presenter.Setters.Add(new Setter(Control.IsTabStopProperty, false));
        flyout.FlyoutPresenterStyle = presenter;
        popover.RequestedTheme = ActualTheme;
        IsOpen = true;
        flyout.ShowAt(hint);

        if (!model.Open)
        {
            await model.Toggle();
        }
    }

    // Closed by its button, Escape, or a press outside it. What was read stays, so the hint keeps its figures.
    private void WhenClosed()
    {
        IsOpen = false;
        model?.Open = false;
        Closed?.Invoke();
    }

    private void Show()
    {
        Visibility = model is null ? Visibility.Collapsed : Visibility.Visible;
        modelWords.Text = model?.ShortModel ?? ReplyUsageViewModel.Label;
        modelMark.Visibility = model?.ShortModel is null ? Visibility.Collapsed : Visibility.Visible;
        figures.Text = model?.Figures ?? string.Empty;
        figures.Visibility = model?.Figures is null ? Visibility.Collapsed : Visibility.Visible;
        modelMark.Foreground = hint.CurrentInk;
        AutomationProperties.SetName(hint, model?.HintName ?? string.Empty);
        ToolTipService.SetToolTip(hint, model?.WrittenBy ?? ReplyUsageViewModel.ShowLabel);
        ShowBody();
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
            body.Children.Add(new Button { Content = "Try again", HorizontalAlignment = HorizontalAlignment.Left, Command = model.ReadCommand });
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
        shown.Foreground = Tokens.Brush(this, "DvText2");
        shown.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetLiveSetting(shown, live);
        return shown;
    }

    #endregion

    #region Fixed Words

    public const string CloseLabel = "Close";

    #endregion
}
