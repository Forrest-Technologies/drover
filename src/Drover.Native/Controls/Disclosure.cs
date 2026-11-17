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

    private readonly PlainButton toggle = new() { HoverFill = "DvBg2", RingInside = true, Radius = "DvBorder" };

    private readonly TextBlock title = Styles.Text(null);

    private readonly ContentPresenter status = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock summary = Styles.Text(null, "DvTextMono");

    private readonly Icon mark = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly ContentPresenter body = new();

    #endregion

    #region Constructors

    public Disclosure()
    {
        title.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightHeading"];
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
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
        toggle.Padding = Tokens.Space("DvSpace4", "DvSpace3");
        toggle.HorizontalAlignment = HorizontalAlignment.Stretch;
        toggle.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        toggle.Click += (_, _) => Open = !Open;

        body.Padding = new(Tokens.Number("DvSpace4"), Tokens.Number("DvSpace2"), Tokens.Number("DvSpace4"), Tokens.Number("DvSpace5"));
        frame.RowDefinitions.Add(new() { Height = GridLength.Auto });
        frame.RowDefinitions.Add(new() { Height = GridLength.Auto });
        SetRow(body, 1);
        frame.Children.Add(toggle);
        frame.Children.Add(body);
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

    public static readonly DependencyProperty OpenProperty = Dp.Register<Disclosure, bool>(nameof(Open), false, section => section.Show());

    public static readonly DependencyProperty BodyProperty = Dp.Register<Disclosure, object?>(nameof(Body), null, section => section.Show());

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

    #endregion

    #region Private Methods

    private void Show()
    {
        title.Text = Title;
        status.Content = Status;
        summary.Text = Summary ?? string.Empty;
        summary.Visibility = !Open && Summary is not null ? Visibility.Visible : Visibility.Collapsed;
        mark.NameOfIcon = Open ? IconName.ChevronDown : IconName.ChevronRight;
        mark.Foreground = Tokens.Brush(this, "DvText3");
        body.Content = Open ? Body : null;
        body.Visibility = Open ? Visibility.Visible : Visibility.Collapsed;
        frame.Cut = Tokens.Number("DvCut");
        frame.Edge = Tokens.Brush(this, Edge ?? (Open ? "DvText3" : "DvLine"));

        // Read out as one control: the title, then the summary, and whether it is open.
        AutomationProperties.SetName(toggle, !Open && Summary is not null ? $"{Title}, {Summary}" : Title);
        AutomationProperties.SetItemStatus(toggle, Open ? "Expanded" : "Collapsed");
    }

    #endregion
}
