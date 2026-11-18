namespace Drover.Native.Controls;

/// <summary>
/// What happened and what to do, in a sentence, with an icon and a color that only repeat the kind. Nothing is
/// shown while the text is empty, but the note is always there: a live region is announced only if it existed
/// before its message arrived.
/// </summary>
public sealed class StatusNote : Grid
{
    #region Private Fields

    private readonly Border box = new() { Visibility = Visibility.Collapsed };

    private readonly Icon icon = new();

    private readonly TextBlock words = Styles.Text(null);

    #endregion

    #region Constructors

    public StatusNote()
    {
        var row = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });

        // The icon sits on the first line of text, whatever the line count.
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new(0, ((Tokens.Number("DvText14") * Tokens.Number("DvLeadingText")) - Tokens.Number("DvIcon16")) / 2, 0, 0);
        SetColumn(words, 1);
        row.Children.Add(icon);
        row.Children.Add(words);

        box.Child = row;
        box.Padding = new(Tokens.Number("DvSpace3"));
        box.BorderThickness = new(Tokens.Number("DvBorder"));
        box.CornerRadius = new(Tokens.Number("DvRadiusControl"));
        Children.Add(box);
        Visibility = Visibility.Collapsed;
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TextProperty = Dp.Register<StatusNote, string?>(nameof(Text), null, note => note.Say());

    public static readonly DependencyProperty KindProperty = Dp.Register<StatusNote, NoteKind>(nameof(Kind), NoteKind.Info, note => note.Paint());

    /// <summary>What happened and what to do, in a sentence. Nothing is shown while this is empty.</summary>
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public NoteKind Kind
    {
        get => (NoteKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    #endregion

    #region Private Methods

    private void Say()
    {
        words.Text = Text ?? string.Empty;
        box.Visibility = string.IsNullOrWhiteSpace(Text) ? Visibility.Collapsed : Visibility.Visible;

        // An empty note takes no place in its column, so no gap is kept for it, as the web's region takes no room.
        Visibility = box.Visibility;
        Paint();

        if (!string.IsNullOrWhiteSpace(Text))
        {
            FrameworkElementAutomationPeer.FromElement(words)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void Paint()
    {
        var (edge, ink, mark) = Kind switch
        {
            NoteKind.Ok => ("DvOk", "DvText", "DvOk"),
            NoteKind.Danger => ("DvDanger", "DvText", "DvDanger"),
            _ => ("DvLine", "DvText2", "DvText2"),
        };

        icon.NameOfIcon = Kind switch
        {
            NoteKind.Ok => IconName.Check,
            NoteKind.Danger => IconName.Warning,
            _ => IconName.Info,
        };
        icon.Foreground = Tokens.Brush(this, mark);
        box.Background = Tokens.Brush(this, "DvBg2");
        box.BorderBrush = Tokens.Brush(this, edge);
        words.Foreground = Tokens.Brush(this, ink);
        AutomationProperties.SetLiveSetting(
            words, Kind is NoteKind.Danger ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
    }

    #endregion
}
