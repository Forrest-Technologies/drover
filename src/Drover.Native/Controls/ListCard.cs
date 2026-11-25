namespace Drover.Native.Controls;

/// <summary>
/// One card for a thing a person opens: a frame, a leading part, the name as the link, one line of facts, a state and
/// its trailing controls. The whole card opens when it can; when nobody handles <see cref="Opened"/> the name is plain
/// text and the card stays quiet under a pointer. An optional block sits beneath the frame, for nested things.
/// </summary>
public sealed class ListCard : StackPanel
{
    #region Private Fields

    private readonly Facet frame = new()
    {
        Role = FacetRole.Frame,
        Cut = Tokens.Number("DvCut"),
        MinHeight = Tokens.Number("DvTargetTouch"),
        Padding = Tokens.Space("DvSpace4", "DvSpace3"),
    };

    private readonly Grid line = new() { ColumnSpacing = Tokens.Number("DvSpace3") };

    private readonly StackPanel text = new() { Spacing = Tokens.Number("DvSpace1") };

    private UIElement? beneath;

    private RowLink? link;

    private Action? opened;

    private bool hovered;

    private bool focused;

    #endregion

    #region Constructors

    public ListCard()
    {
        Spacing = Tokens.Number("DvSpace3");
        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        frame.Children.Add(line);
        Children.Add(frame);

        frame.PointerEntered += (_, _) => Hover(true);
        frame.PointerExited += (_, _) => Hover(false);
        frame.PointerCanceled += (_, _) => Hover(false);
        frame.Tapped += (_, args) => OpenFrom(args.OriginalSource as DependencyObject);
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TitleProperty =
        Dp.Register<ListCard, string>(nameof(Title), string.Empty, card => card.Apply());

    public static readonly DependencyProperty FactsProperty =
        Dp.Register<ListCard, string?>(nameof(Facts), null, card => card.Apply());

    public static readonly DependencyProperty LeadingProperty =
        Dp.Register<ListCard, UIElement?>(nameof(Leading), null, card => card.Apply());

    public static readonly DependencyProperty StatusProperty =
        Dp.Register<ListCard, UIElement?>(nameof(Status), null, card => card.Apply());

    public static readonly DependencyProperty TrailingProperty =
        Dp.Register<ListCard, UIElement?>(nameof(Trailing), null, card => card.Apply());

    public static readonly DependencyProperty BeneathProperty =
        Dp.Register<ListCard, UIElement?>(nameof(Beneath), null, card => card.PlaceBeneath());

    /// <summary>The automation id of the name's link, which a screen sets to find the card in a check.</summary>
    public static readonly DependencyProperty LinkAutomationIdProperty =
        Dp.Register<ListCard, string?>(nameof(LinkAutomationId), null, card => card.Apply());

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Facts
    {
        get => (string?)GetValue(FactsProperty);
        set => SetValue(FactsProperty, value);
    }

    public UIElement? Leading
    {
        get => (UIElement?)GetValue(LeadingProperty);
        set => SetValue(LeadingProperty, value);
    }

    /// <summary>A short word about the card's state, beside the trailing controls.</summary>
    public UIElement? Status
    {
        get => (UIElement?)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>Controls that keep their own action, and are not part of the card's tap target.</summary>
    public UIElement? Trailing
    {
        get => (UIElement?)GetValue(TrailingProperty);
        set => SetValue(TrailingProperty, value);
    }

    public UIElement? Beneath
    {
        get => (UIElement?)GetValue(BeneathProperty);
        set => SetValue(BeneathProperty, value);
    }

    public string? LinkAutomationId
    {
        get => (string?)GetValue(LinkAutomationIdProperty);
        set => SetValue(LinkAutomationIdProperty, value);
    }

    /// <summary>Raised when the card is tapped outside its trailing controls or its name is pressed.</summary>
    public event Action? Opened
    {
        add
        {
            opened += value;
            Apply();
        }
        remove
        {
            opened -= value;
            Apply();
        }
    }

    #endregion

    #region Private Methods

    private void Apply()
    {
        line.Children.Clear();
        text.Children.Clear();
        link = null;
        hovered = false;
        focused = false;

        if (Leading is { } leading)
        {
            leading.SetValue(Grid.ColumnProperty, 0);
            line.Children.Add(leading);
        }

        var name = Styles.Text(Title, "DvTextH3");
        AutomationProperties.SetHeadingLevel(name, AutomationHeadingLevel.None);
        name.HorizontalAlignment = HorizontalAlignment.Stretch;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;

        if (opened is not null)
        {
            var button = new RowLink
            {
                Content = name,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(button, Title);
            if (LinkAutomationId is { Length: > 0 } id)
            {
                AutomationProperties.SetAutomationId(button, id);
            }

            button.Click += (_, _) => opened?.Invoke();
            button.KeyboardFocus += value =>
            {
                focused = value;
                Paint();
            };
            link = button;
            text.Children.Add(button);
        }
        else
        {
            text.Children.Add(name);
        }

        if (Facts is { Length: > 0 } facts)
        {
            var lineOfFacts = BotParts.Mono(facts).Inked("DvText2");
            lineOfFacts.HorizontalAlignment = HorizontalAlignment.Stretch;
            lineOfFacts.TextWrapping = TextWrapping.NoWrap;
            lineOfFacts.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(lineOfFacts);
        }

        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        line.Children.Add(text);

        if (Status is { } status)
        {
            status.SetValue(Grid.ColumnProperty, 2);
            line.Children.Add(status);
        }

        if (Trailing is { } trailing)
        {
            trailing.SetValue(Grid.ColumnProperty, 3);
            line.Children.Add(trailing);
        }

        Paint();
    }

    private void PlaceBeneath()
    {
        if (beneath is not null)
        {
            Children.Remove(beneath);
        }

        beneath = Beneath;
        if (beneath is not null)
        {
            Children.Add(beneath);
        }
    }

    private void Hover(bool value)
    {
        if (opened is null)
        {
            return;
        }

        hovered = value;
        Paint();
    }

    private void OpenFrom(DependencyObject? source)
    {
        if (opened is null || Within(link, source) || Within(Trailing, source))
        {
            return;
        }

        opened();
    }

    private void Paint()
    {
        var hovering = hovered && opened is not null;
        frame.Fill = Tokens.Brush(this, hovering ? "DvBg2" : "DvBg1");
        frame.Edge = Tokens.Brush(this, hovering ? "DvText3" : "DvLine");
        frame.Ring = focused && opened is not null ? Tokens.Brush(this, "DvEmber") : null;
    }

    private static bool Within(UIElement? ancestor, DependencyObject? source)
    {
        if (ancestor is null)
        {
            return false;
        }

        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    #endregion
}
