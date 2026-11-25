namespace Drover.Native.Chat;

/// <summary>
/// How a failed run ends in the conversation: what failed in a sentence, one way forward, and what the server
/// reported behind a disclosure for whoever needs it. The report is a sentence of machine text, so it wraps.
/// </summary>
public sealed class FailureLine : Grid
{
    #region Private Fields

    private readonly Facet frame = new();

    private readonly TextBlock title = Styles.Text(null);

    private readonly Button retry = new() { Kind = ButtonKind.Primary, Content = "Try again", HorizontalAlignment = HorizontalAlignment.Left };

    private readonly Link openLimits = new()
    {
        Text = AttentionWords.OpenLimitsLabel,
        MinHeight = Targets.Minimum,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Disclosure report = new() { Title = "What the server reported" };

    private readonly Border reportBox = new();

    private readonly TextBlock reported = Styles.Text(null, "DvTextMono");

    #endregion

    #region Constructors

    public FailureLine()
    {
        title.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        title.LineHeight = Tokens.Number("DvText14") * Tokens.Number("DvLeadingHeading");
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level3);
        AutomationProperties.SetLandmarkType(this, AutomationLandmarkType.Custom);

        reported.IsTextSelectionEnabled = true;
        reportBox.Child = reported;
        reportBox.Padding = new(Tokens.Number("DvSpace3"));
        reportBox.CornerRadius = new(Tokens.Number("DvRadiusControl"));
        report.Body = reportBox;

        // Not disabled while asking: the button keeps focus, says it is busy, and a second press does nothing.
        retry.Click += (_, _) =>
        {
            if (!Retrying)
            {
                Retry?.Invoke(this, EventArgs.Empty);
            }
        };

        var actions = new FlowPanel { Gap = Tokens.Number("DvSpace3") };
        actions.Children.Add(retry);
        actions.Children.Add(openLimits);

        var column = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        column.Children.Add(title);
        column.Children.Add(actions);
        column.Children.Add(report);
        frame.Padding = new(Tokens.Number("DvSpace4"), Tokens.Number("DvSpace3"), Tokens.Number("DvSpace4"), Tokens.Number("DvSpace4"));
        frame.Children.Add(column);
        Children.Add(frame);
        this.WhenShownOrRethemed(Paint);
        Show();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty CauseProperty =
        Dp.Register<FailureLine, FailureCause>(nameof(Cause), default, line => line.Show());

    public static readonly DependencyProperty BotNameProperty = Dp.Register<FailureLine, string>(nameof(BotName), string.Empty, line => line.Show());

    public static readonly DependencyProperty ReportedProperty = Dp.Register<FailureLine, string?>(nameof(Reported), null, line => line.Show());

    public static readonly DependencyProperty LimitProperty = Dp.Register<FailureLine, RunFailureLimit?>(nameof(Limit), null, line => line.Show());

    public static readonly DependencyProperty LimitsHrefProperty = Dp.Register<FailureLine, string?>(nameof(LimitsHref), null, line => line.Show());

    public static readonly DependencyProperty RetryingProperty = Dp.Register<FailureLine, bool>(nameof(Retrying), false, line => line.Show());

    public FailureCause Cause
    {
        get => (FailureCause)GetValue(CauseProperty);
        set => SetValue(CauseProperty, value);
    }

    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    /// <summary>The error as the run's log holds it, already redacted. Shown only when asked for.</summary>
    public string? Reported
    {
        get => (string?)GetValue(ReportedProperty);
        set => SetValue(ReportedProperty, value);
    }

    /// <summary>The Drover limit the run stopped at, when the server reported one.</summary>
    public RunFailureLimit? Limit
    {
        get => (RunFailureLimit?)GetValue(LimitProperty);
        set => SetValue(LimitProperty, value);
    }

    /// <summary>The bot's Limits settings, for a person who can edit the bot when a limit stopped the run.</summary>
    public string? LimitsHref
    {
        get => (string?)GetValue(LimitsHrefProperty);
        set => SetValue(LimitsHrefProperty, value);
    }

    public bool Retrying
    {
        get => (bool)GetValue(RetryingProperty);
        set => SetValue(RetryingProperty, value);
    }

    /// <summary>Raised to start a new run from the same message.</summary>
    public event EventHandler? Retry;

    #endregion

    #region Public Methods

    /// <summary>Puts the focus on the way forward, for when the line takes the place of what the person was using.</summary>
    public void TakeFocus() => retry.TakeFocus();

    #endregion

    #region Private Methods

    private void Show()
    {
        title.Text = AttentionWords.Failure(Cause, BotName, Limit);
        AutomationProperties.SetName(this, title.Text);
        AutomationProperties.SetItemStatus(retry, Retrying ? "Busy" : string.Empty);
        var limitsHref = Cause is FailureCause.BudgetReached && !string.IsNullOrWhiteSpace(LimitsHref) ? LimitsHref : null;
        openLimits.Address = limitsHref;
        openLimits.Visibility = limitsHref is null ? Visibility.Collapsed : Visibility.Visible;
        reported.Text = Reported ?? string.Empty;
        report.Visibility = string.IsNullOrWhiteSpace(Reported) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Paint()
    {
        frame.Edge = Tokens.Brush(this, "DvDanger");
        reportBox.Background = Tokens.Brush(this, "DvBg2");
        reported.Foreground = Tokens.Brush(this, "DvText2");
    }

    #endregion
}
