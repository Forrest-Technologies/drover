namespace Drover.Native.Chat;

/// <summary>
/// Work one bot handed to a bot beneath it, in the parent's conversation. Closed: who asked whom for what, and the
/// state in words. Open: the whole task, the child's own activity (passed in, so it is the same message and tool
/// pieces the main conversation uses), and the child's answer. The card's edge is violet while the work is out, then
/// the result's color. The pulse itself belongs to the crew connector, not to this card.
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(ChildContent))]
public sealed class DelegationCard : Grid
{
    #region Private Fields

    private const int HeadSize = 32;

    private readonly DelegationCardViewModel model = new();

    private readonly Batch batch;

    // Neither the head nor the card shows before the card has drawn the work it was given.
    private readonly Avatar head = new() { Size = HeadSize, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };

    private readonly Disclosure section = new() { TitleWeight = "DvWeightLabel", Visibility = Visibility.Collapsed };

    private readonly Badge state = new() { Wraps = true };

    private readonly StackPanel body = new() { Spacing = Tokens.Number("DvSpace4") };

    private readonly ContentPresenter work = new();

    private readonly Border workBox = new();

    private readonly MarkdownView answer = new();

    private bool phone;

    private bool nested;

    #endregion

    #region Constructors

    public DelegationCard()
    {
        batch = new(this, Apply, Draw);

        // The child's work is set in from the card's edge by one line, not by a second frame.
        workBox.Child = work;
        workBox.BorderThickness = new(Tokens.Number("DvBorder"), 0, 0, 0);

        section.Status = state;
        section.Body = body;
        section.RegisterPropertyChangedCallback(Disclosure.OpenProperty, (_, _) => model.Open = section.Open);

        ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        Children.Add(head);
        Children.Add(section);

        model.PropertyChanged += (_, _) => batch.Draw();
        Loaded += (_, _) => nested = IsInsideHandedWork();
        Pieces.WhenPhoneOrNot(this, Fit);
        this.WhenShownOrRethemed(batch.Draw);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty ItemProperty = Dp.Register<DelegationCard, DelegationItem?>(nameof(Item), null, card => card.batch.Values());

    public static readonly DependencyProperty ChildContentProperty =
        Dp.Register<DelegationCard, object?>(nameof(ChildContent), null, card => card.batch.Values());

    /// <summary>The work handed over. Required.</summary>
    public DelegationItem? Item
    {
        get => (DelegationItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    /// <summary>
    /// The child's activity in order: its words and its tool cards, including a call that waits for a person.
    /// </summary>
    public object? ChildContent
    {
        get => GetValue(ChildContentProperty);
        set => SetValue(ChildContentProperty, value);
    }

    #endregion

    #region Layout

    // The card is drawn from the work it was given before it is first measured, so it never shows as an empty frame.
    protected override Size MeasureOverride(Size availableSize)
    {
        batch.DrawIfWaiting();
        return base.MeasureOverride(availableSize);
    }

    #endregion

    #region Private Methods

    private void Apply()
    {
        if (Item is not { } item)
        {
            return;
        }

        (model.Item, model.HasWork) = (item, ChildContent is not null);
        model.ParametersSet();
    }

    // The child's head stands beside its card, the way a bot's head stands beside its replies. On a phone it stands
    // above the card instead, so each level of work handed further down costs only the card's edge and one set-in
    // line, and three levels still read at 360.
    private void Fit()
    {
        phone = Pieces.OnPhone(this);
        SetColumn(section, phone ? 0 : 1);
        SetColumnSpan(section, phone ? 2 : 1);
        SetRow(section, phone ? 1 : 0);
        ColumnSpacing = Tokens.Number("DvSpace2");
        RowSpacing = phone ? Tokens.Number("DvSpace1") : 0;
        head.Margin = new(0, phone ? 0 : Tokens.Number("DvSpace1"), 0, 0);
        workBox.Padding = new(Tokens.Number(phone ? "DvSpace2" : "DvSpace3"), 0, 0, 0);

        // Work handed further down sits inside another card, which already gives it room: on a phone its own card
        // keeps a narrower inner margin.
        section.Inset = phone && nested ? "DvSpace3" : "DvSpace4";
    }

    private bool IsInsideHandedWork()
    {
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is DelegationCard)
            {
                return true;
            }
        }

        return false;
    }

    private void Draw()
    {
        if (Item is null || model.Item is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = head.Visibility = section.Visibility = Visibility.Visible;
        head.BotName = model.Item.ChildName;
        head.Hue = model.Hue;
        head.State = model.Head;
        section.Title = model.Title;
        section.Edge = model.Edge;
        section.Open = model.Open;
        state.Text = model.State;
        state.Kind = model.StateKind;
        workBox.BorderBrush = Tokens.Brush(this, "DvLine");
        Fit();

        if (model.Open)
        {
            DrawBody();
        }
    }

    private void DrawBody()
    {
        body.Children.Clear();

        if (model.IsRefused)
        {
            body.Children.Add(Line(model.RefusedLine));

            if (model.LimitsHref is { } limitsAddress && model.LimitsLinkText is { } limitsText)
            {
                body.Children.Add(new Link { Text = limitsText, Address = limitsAddress, MinHeight = Targets.Minimum });
            }

            return;
        }

        body.Children.Add(Pieces.Part(model.TaskTitle, Styles.Text(model.Item.Task)));

        if (model.ElsewhereLine is { } elsewhere)
        {
            body.Children.Add(Line(elsewhere));
        }
        else if (model.ShowsWork)
        {
            work.Content = ChildContent;
            body.Children.Add(Pieces.Part(model.WorkTitle, Pieces.Detached(workBox)));
        }

        if (model.Answer is { } text)
        {
            answer.Text = text;
            body.Children.Add(Pieces.Part(model.AnswerTitle, Pieces.Detached(answer)));
        }

        if (model.LinkHref is { } address)
        {
            body.Children.Add(new Link { Text = model.LinkText, Address = address, Ink = "DvText2", HoverInk = "DvText2", MinHeight = Targets.Minimum });
        }
    }

    private static TextBlock Line(string text) => Styles.Text(text, "DvTextSecondary");

    #endregion
}
