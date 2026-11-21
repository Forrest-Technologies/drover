namespace Drover.Native.Chat;

/// <summary>
/// One quiet line under a finished reply: copy the reply, ask for another, switch between its versions, and a hint of
/// what wrote it and what it used. The hint is a button; pressed, a small popover holds the model's whole name and the
/// whole of what the reply used, and closes on its close button, Escape, or a press outside it. What the reply used is
/// read only when asked for: a conversation with many replies makes no request until a person opens one. On a narrow
/// screen the hint, last on the line, moves to a line of its own before any button does.
/// </summary>
public sealed class ReplyFooter : Grid
{
    #region Private Fields

    private readonly FlowPanel line = new() { Gap = Tokens.Number("DvSpace1") };

    private readonly CopyButton copy = new() { Label = CopyLabel, Ink = "DvText3", VerticalAlignment = VerticalAlignment.Center };

    private readonly IconButton regenerate = new()
    {
        Icon = IconName.Regenerate,
        Label = ChatRows.RegenerateLabel,
        Ink = "DvText3",
        VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed,
    };

    // The versions' switch stands here when the message is a reply that has versions.
    private readonly StackPanel versionsSlot = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly PlainButton hint = new()
    {
        Ink = "DvText3",
        HoverInk = "DvText2",
        HoverFill = null,
        Radius = null,
        MinHeight = Targets.Minimum,
        Padding = new(Tokens.Number("DvSpace1"), 0, Tokens.Number("DvSpace1"), 0),
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = Tokens.Number("DvText12"),
    };

    private readonly Icon modelMark = new() { NameOfIcon = IconName.Model, VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock modelWords = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

    private readonly TextBlock figures = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly StackPanel body = new() { Spacing = Tokens.Number("DvSpace1") };

    private readonly IconButton close = new() { Icon = IconName.Close, Label = CloseLabel, Ink = "DvText3" };

    private readonly StackPanel popover = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly Flyout flyout = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };

    private ReplyUsageViewModel? model;

    private string copyText = string.Empty;

    #endregion

    #region Constructors

    public ReplyFooter()
    {
        var hintRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace1") };
        hintRow.Children.Add(modelMark);
        hintRow.Children.Add(modelWords);
        hintRow.Children.Add(figures);
        foreach (var part in hintRow.Children)
        {
            AutomationProperties.SetAccessibilityView(part, AccessibilityView.Raw);
        }

        hint.Content = hintRow;
        hint.Click += async (_, _) => await Open();
        copy.Text = () => copyText;

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

        popover.Children.Add(head);
        popover.Children.Add(body);
        AutomationProperties.SetName(popover, ReplyUsageViewModel.Label);
        flyout.Content = popover;
        flyout.Opened += (_, _) => close.TakeFocus();
        flyout.Closed += (_, _) => Closed();

        regenerate.Click += async (_, _) => await Regenerate();

        // The parts stand in the order a footer's parts are named, so the line and its tests agree.
        foreach (var part in Enum.GetValues<FooterPart>())
        {
            line.Children.Add(PartOf(part));
        }

        Children.Add(line);
        this.WhenShownOrRethemed(Show);
    }

    #endregion

    #region Properties

    /// <summary>What the reply used, and the model that wrote it. Without it the line shows no hint.</summary>
    public ReplyUsageViewModel? Model
    {
        get => model;
        set
        {
            model?.PropertyChanged -= Changed;
            model = value;
            model?.PropertyChanged += Changed;
            Show();
        }
    }

    /// <summary>True when the line holds anything to use, so the reply shows it.</summary>
    public bool HasAny =>
        copy.Visibility is Visibility.Visible
        || regenerate.Visibility is Visibility.Visible
        || hint.Visibility is Visibility.Visible
        || versionsSlot.Children.Any(part => part.Visibility is Visibility.Visible);

    /// <summary>Asks for another reply. Without it, the line offers none.</summary>
    public Func<Task>? OnRegenerate { get; set; }

    #endregion

    #region Public Methods

    /// <summary>Puts the versions' switch on this line.</summary>
    public void Hold(UIElement versions)
    {
        if (!versionsSlot.Children.Contains(versions))
        {
            versionsSlot.Children.Add(versions);
        }
    }

    /// <summary>Shows the parts of the line the reply has (<see cref="ChatRows.FooterParts"/>), with its words to copy.</summary>
    public void Show(string replyText, IReadOnlyList<FooterPart> parts)
    {
        copyText = parts.Contains(FooterPart.Copy) ? replyText : string.Empty;
        copy.Visibility = copyText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        regenerate.Visibility = parts.Contains(FooterPart.Regenerate) && OnRegenerate is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Puts the focus on the regenerate icon.</summary>
    public void TakeFocus() => regenerate.TakeFocus();

    #endregion

    #region Private Methods

    private FrameworkElement PartOf(FooterPart part) => part switch
    {
        FooterPart.Copy => copy,
        FooterPart.Regenerate => regenerate,
        FooterPart.Versions => versionsSlot,
        _ => hint,
    };

    private async Task Regenerate()
    {
        if (OnRegenerate is { } regenerateReply)
        {
            await regenerateReply();
        }
    }

    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs change) => Show();

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
        flyout.ShowAt(hint);

        if (!model.Open)
        {
            await model.Toggle();
        }
    }

    // Closed by its button, Escape, or a press outside it. What was read stays, so the hint keeps its figures.
    private void Closed() => model?.Open = false;

    private void Show()
    {
        hint.Visibility = model is null ? Visibility.Collapsed : Visibility.Visible;
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

    public const string CopyLabel = "Copy reply";

    public const string CloseLabel = "Close";

    #endregion
}
