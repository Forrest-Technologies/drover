namespace Drover.Native.Controls;

/// <summary>
/// One event of a timeline: what happened in plain words, a line about it, and its state in words. It is kept while
/// its event is shown and told what changed, so the focus stays on it while a live run's next events arrive.
/// </summary>
internal sealed class TimelineNodeButton : Pressable
{
    #region Private Fields

    private readonly TextBlock label = Styles.Text(null);

    private readonly TextBlock summary = Styles.Text(null, "DvTextMono");

    private readonly Badge state = new() { Wraps = true };

    private readonly Grid layers = new();

    private bool isOpen;

    private string? edge;

    private string? liveMark;

    #endregion

    #region Constructors

    public TimelineNodeButton(TimelineNode node, bool isOpen, string? edge)
        : base("DvFacetTemplate")
    {
        MinHeight = Targets.Minimum;
        Padding = Tokens.Space("DvSpace3", "DvSpace2");
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        label.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        summary.TextWrapping = TextWrapping.NoWrap;
        summary.TextTrimming = TextTrimming.CharacterEllipsis;

        var column = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
        column.Children.Add(label);
        column.Children.Add(summary);
        column.Children.Add(state);
        layers.Children.Add(column);
        Content = layers;
        Node = node;
        Show(node, isOpen, edge);
    }

    #endregion

    #region Properties

    /// <summary>The event as it was last shown.</summary>
    public TimelineNode Node { get; private set; }

    #endregion

    #region Public Methods

    /// <summary>Shows the event as it is now, in place.</summary>
    public void Show(TimelineNode node, bool isOpen, string? edge)
    {
        (Node, this.isOpen, this.edge) = (node, isOpen, edge);
        label.Text = node.Event.Label;
        summary.Text = node.Event.Summary ?? string.Empty;
        summary.Visibility = node.Event.Summary is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        state.Text = Timelines.State(node.Event.State);
        state.Kind = Timelines.Kind(node.Event.State);
        ShowLiveMark(node.Event);
        Repaint();
    }

    #endregion

    #region Private Methods

    // A step being thought through carries a band of light on its top edge that breathes, like the bot's visor.
    // A running tool carries the traveling line. With reduced motion both hold still. A mark that is still the
    // right one is left running.
    private void ShowLiveMark(TimelineEvent happening)
    {
        var wanted = happening switch
        {
            { State: TimelineState.Live, Kind: TimelineKind.Tool } => nameof(TravelLine),
            { State: TimelineState.Live, Kind: TimelineKind.Step, Writing: false } => nameof(LiveLight),
            _ => null,
        };
        if (wanted == liveMark)
        {
            return;
        }

        liveMark = wanted;
        while (layers.Children.Count > 1)
        {
            layers.Children.RemoveAt(layers.Children.Count - 1);
        }

        var lift = new Thickness(-Tokens.Number("DvSpace3"), -Tokens.Number("DvSpace2"), -Tokens.Number("DvSpace3"), 0);
        if (wanted is nameof(TravelLine))
        {
            layers.Children.Add(new TravelLine { IsRunning = true, Margin = lift });
        }
        else if (wanted is nameof(LiveLight))
        {
            var breath = new LiveLight
            {
                State = LiveState.Thinking,
                Height = Tokens.Number("DvFocusWidth"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = lift,
            };
            layers.Children.Add(breath);
        }
    }

    protected override void Paint()
    {
        var frame = (Facet)Frame!;
        frame.Role = FacetRole.Control;
        frame.Fill = Tokens.Brush(this, isOpen || IsHovered ? "DvBg3" : "DvBg2");
        frame.Edge = Tokens.Brush(this, edge ?? "DvLine");
        frame.Ring = HasKeyboardFocus ? Tokens.Brush(this, "DvEmber") : null;
        Foreground = Tokens.Brush(this, "DvText");
        label.Foreground = Tokens.Brush(this, "DvText");
    }

    #endregion
}
