namespace Drover.Native.Controls;

/// <summary>
/// A section that opens and closes, on a faceted frame. While it is closed, one line says what it is set to, inside
/// the control that opens it, so the line is read out with the title.
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(Body))]
public sealed class Disclosure : Grid
{
    #region Private Fields

    private readonly Facet frame = new();

    private readonly PlainButton toggle = new() { HoverFill = "DvBg2", RingInside = true, Radius = null };

    private readonly TextBlock title = Styles.Text(null);

    private readonly ContentPresenter status = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock summary = Styles.Text(null, "DvTextMono");

    private readonly Icon mark = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly ContentPresenter body = new();

    private readonly TravelLine travel = new();

    #endregion

    #region Constructors

    public Disclosure()
    {
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);

        var titleRow = new FlowPanel { Gap = Tokens.Number("DvSpace2") };
        titleRow.Children.Add(title);
        titleRow.Children.Add(status);
        var words = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
        words.Children.Add(titleRow);
        words.Children.Add(summary);

        var content = new Grid { ColumnSpacing = Tokens.Number("DvSpace3") };
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        SetColumn(mark, 1);
        content.Children.Add(words);
        content.Children.Add(mark);

        toggle.Content = content;
        toggle.MinHeight = Tokens.Number("DvTargetTouch");
        toggle.HorizontalAlignment = HorizontalAlignment.Stretch;
        toggle.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        toggle.Click += (_, _) => Open = !Open;

        frame.RowDefinitions.Add(new() { Height = GridLength.Auto });
        frame.RowDefinitions.Add(new() { Height = GridLength.Auto });
        SetRow(body, 1);
        frame.Children.Add(toggle);
        frame.Children.Add(body);
        frame.Children.Add(travel);
        Children.Add(frame);
        Show();
        this.WhenShownOrRethemed(Show);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TitleProperty = Dp.Register<Disclosure, string>(nameof(Title), string.Empty, section => section.Show());

    public static readonly DependencyProperty SummaryProperty = Dp.Register<Disclosure, string?>(nameof(Summary), null, section => section.Show());

    public static readonly DependencyProperty StatusProperty = Dp.Register<Disclosure, object?>(nameof(Status), null, section => section.Show());

    public static readonly DependencyProperty EdgeProperty = Dp.Register<Disclosure, string?>(nameof(Edge), null, section => section.Show());

    public static readonly DependencyProperty RunningProperty =
        Dp.Register<Disclosure, bool>(nameof(Running), false, section => section.travel.IsRunning = section.Running);

    public static readonly DependencyProperty OpenProperty = Dp.Register<Disclosure, bool>(nameof(Open), false, section => section.Show());

    public static readonly DependencyProperty BodyProperty = Dp.Register<Disclosure, object?>(nameof(Body), null, section => section.Show());

    public static readonly DependencyProperty TitleWeightProperty =
        Dp.Register<Disclosure, string>(nameof(TitleWeight), "DvWeightHeading", section => section.Show());

    public static readonly DependencyProperty SummaryWrapsProperty = Dp.Register<Disclosure, bool>(nameof(SummaryWraps), true, section => section.Show());

    public static readonly DependencyProperty InsetProperty = Dp.Register<Disclosure, string>(nameof(Inset), "DvSpace4", section => section.Show());

    /// <summary>What the section holds, in a word or two.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>One line that says what the section is set to, shown while it is closed.</summary>
    public string? Summary
    {
        get => (string?)GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    /// <summary>What the thing the section is about is doing now, in words, shown after the title: a badge, usually.</summary>
    public object? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>A color token for the frame's edge, for a section that marks a state on its edge. The usual edge when null.</summary>
    public string? Edge
    {
        get => (string?)GetValue(EdgeProperty);
        set => SetValue(EdgeProperty, value);
    }

    /// <summary>True while a tool the section is about runs: the line of a running tool travels its top edge.</summary>
    public bool Running
    {
        get => (bool)GetValue(RunningProperty);
        set => SetValue(RunningProperty, value);
    }

    /// <summary>Whether the section is open. It can be bound both ways.</summary>
    public bool Open
    {
        get => (bool)GetValue(OpenProperty);
        set => SetValue(OpenProperty, value);
    }

    /// <summary>What the section holds. It is in the screen only while the section is open.</summary>
    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    /// <summary>The weight of the title, as a token name. A card in a conversation is lighter than a section of a form.</summary>
    public string TitleWeight
    {
        get => (string)GetValue(TitleWeightProperty);
        set => SetValue(TitleWeightProperty, value);
    }

    /// <summary>False keeps the summary to one line, cut short with an ellipsis, as a card's line of arguments is.</summary>
    public bool SummaryWraps
    {
        get => (bool)GetValue(SummaryWrapsProperty);
        set => SetValue(SummaryWrapsProperty, value);
    }

    /// <summary>The space between the frame's sides and what is in it, as a token name. A card set inside another keeps less on a phone.</summary>
    public string Inset
    {
        get => (string)GetValue(InsetProperty);
        set => SetValue(InsetProperty, value);
    }

    #endregion

    #region Private Methods

    private void Show()
    {
        title.Text = Title;
        title.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary[TitleWeight];
        summary.TextWrapping = SummaryWraps ? TextWrapping.Wrap : TextWrapping.NoWrap;
        summary.TextTrimming = SummaryWraps ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        toggle.Padding = Tokens.Space(Inset, "DvSpace3");
        body.Padding = new(Tokens.Number(Inset), Tokens.Number("DvSpace2"), Tokens.Number(Inset), Tokens.Number("DvSpace5"));
        status.Content = Status;
        summary.Text = Summary ?? string.Empty;
        summary.Visibility = !Open && Summary is not null ? Visibility.Visible : Visibility.Collapsed;
        mark.NameOfIcon = Open ? IconName.ChevronDown : IconName.ChevronRight;
        mark.Foreground = Tokens.Brush(this, "DvText3");
        body.Content = Open ? Body : null;
        body.Visibility = Open ? Visibility.Visible : Visibility.Collapsed;
        frame.Role = FacetRole.Frame;
        frame.Edge = Tokens.Brush(this, Edge ?? (Open ? "DvText3" : "DvLine"));

        // Read out as one control: the title, then the summary, and whether it is open.
        AutomationProperties.SetName(toggle, !Open && Summary is not null ? $"{Title}, {Summary}" : Title);
        AutomationProperties.SetItemStatus(toggle, Open ? "Expanded" : "Collapsed");
    }

    #endregion
}
