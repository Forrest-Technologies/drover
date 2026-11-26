namespace Drover.Native.Chat;

/// <summary>
/// Shown in place of a completed bot reply that contains only a raw tool-call protocol envelope instead of an answer or
/// a structured tool call. Explains in plain words that no tool ran, offers the existing retry flow when available, and
/// keeps the original response behind a collapsed disclosure for inspection.
/// </summary>
public sealed class UnusableToolRequestCard : Grid
{
    #region Private Fields

    private readonly Facet frame = new();

    private readonly TextBlock title = Styles.Text(null);

    private readonly TextBlock detail = Styles.Text(null, "DvTextSecondary");

    private readonly Button retry = new()
    {
        Kind = ButtonKind.Primary,
        Content = UnusableToolRequests.RetryLabel,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private readonly Disclosure disclosure = new() { Title = UnusableToolRequests.OriginalResponseTitle };

    private readonly Border originalBox = new();

    private readonly TextBlock original = Styles.Text(null, "DvTextMono");

    private string? shownKey;

    #endregion

    #region Constructors

    public UnusableToolRequestCard()
    {
        title.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        title.LineHeight = Tokens.Number("DvText14") * Tokens.Number("DvLeadingHeading");
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level3);
        AutomationProperties.SetLandmarkType(this, AutomationLandmarkType.Custom);

        original.IsTextSelectionEnabled = true;
        originalBox.Child = original;
        originalBox.Padding = new(Tokens.Number("DvSpace3"));
        originalBox.CornerRadius = new(Tokens.Number("DvRadiusControl"));
        disclosure.Body = originalBox;

        retry.Click += (_, _) =>
        {
            if (CanRetry)
            {
                Retry?.Invoke(this, EventArgs.Empty);
            }
        };

        var copy = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
        copy.Children.Add(title);
        copy.Children.Add(detail);

        var column = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        column.Children.Add(copy);
        column.Children.Add(retry);
        column.Children.Add(disclosure);

        frame.Padding = new(Tokens.Number("DvSpace4"), Tokens.Number("DvSpace3"), Tokens.Number("DvSpace4"), Tokens.Number("DvSpace4"));
        frame.Children.Add(column);
        Children.Add(frame);
        this.WhenShownOrRethemed(Paint);
        Show();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty MessageKeyProperty =
        Dp.Register<UnusableToolRequestCard, string?>(nameof(MessageKey), null, card => card.Show());

    public static readonly DependencyProperty BotNameProperty =
        Dp.Register<UnusableToolRequestCard, string>(nameof(BotName), string.Empty, card => card.Show());

    public static readonly DependencyProperty OriginalResponseProperty =
        Dp.Register<UnusableToolRequestCard, string?>(nameof(OriginalResponse), null, card => card.Show());

    public static readonly DependencyProperty CanRetryProperty =
        Dp.Register<UnusableToolRequestCard, bool>(nameof(CanRetry), false, card => card.Show());

    public string? MessageKey
    {
        get => (string?)GetValue(MessageKeyProperty);
        set => SetValue(MessageKeyProperty, value);
    }

    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    /// <summary>The raw response text as stored on the message. Shown only when the person opens the disclosure.</summary>
    public string? OriginalResponse
    {
        get => (string?)GetValue(OriginalResponseProperty);
        set => SetValue(OriginalResponseProperty, value);
    }

    /// <summary>True when this reply may be tried again through the existing regenerate flow.</summary>
    public bool CanRetry
    {
        get => (bool)GetValue(CanRetryProperty);
        set => SetValue(CanRetryProperty, value);
    }

    /// <summary>Raised when the person asks for another reply.</summary>
    public event EventHandler? Retry;

    #endregion

    #region Private Methods

    private void Show()
    {
        if (MessageKey != shownKey)
        {
            shownKey = MessageKey;
            disclosure.Open = false;
        }

        title.Text = UnusableToolRequests.Title(BotName);
        detail.Text = UnusableToolRequests.Detail(CanRetry);
        AutomationProperties.SetName(this, title.Text);
        retry.Visibility = CanRetry ? Visibility.Visible : Visibility.Collapsed;
        original.Text = OriginalResponse ?? string.Empty;
        disclosure.Visibility = string.IsNullOrWhiteSpace(OriginalResponse) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Paint()
    {
        frame.Edge = Tokens.Brush(this, "DvWarn");
        originalBox.Background = Tokens.Brush(this, "DvBg2");
        original.Foreground = Tokens.Brush(this, "DvText2");
    }

    #endregion
}
