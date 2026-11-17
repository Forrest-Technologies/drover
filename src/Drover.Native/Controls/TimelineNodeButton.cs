namespace Drover.Native.Controls;

/// <summary>One event of a timeline: what happened in plain words, a line about it, and its state in words.</summary>
internal sealed class TimelineNodeButton : Pressable
{
    #region Private Fields

    private readonly bool isOpen;

    private readonly string? edge;

    private readonly TextBlock label = Styles.Text(null);

    private readonly TextBlock summary = Styles.Text(null, "DvTextMono");

    #endregion

    #region Constructors

    public TimelineNodeButton(TimelineNode node, bool isOpen, string? edge)
        : base("DvFacetTemplate")
    {
        (this.isOpen, this.edge) = (isOpen, edge);
        MinHeight = Tokens.Number("DvTargetTouch");
        Padding = Tokens.Space("DvSpace3", "DvSpace2");
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        label.Text = node.Event.Label;
        label.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        summary.Text = node.Event.Summary ?? string.Empty;
        summary.TextWrapping = TextWrapping.NoWrap;
        summary.TextTrimming = TextTrimming.CharacterEllipsis;
        summary.Visibility = node.Event.Summary is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

        var column = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
        column.Children.Add(label);
        column.Children.Add(summary);
        column.Children.Add(new Badge { Text = Timelines.State(node.Event.State), Kind = Timelines.Kind(node.Event.State), Wraps = true });

        var layers = new Grid();
        layers.Children.Add(column);
        AddLiveMark(layers, node.Event);
        Content = layers;
    }

    #endregion

    #region Private Methods

    // A step being thought through carries a band of light on its top edge that breathes, like the bot's visor.
    // A running tool carries the traveling line. With reduced motion both hold still.
    private void AddLiveMark(Grid layers, TimelineEvent happening)
    {
        var lift = new Thickness(-Tokens.Number("DvSpace3"), -Tokens.Number("DvSpace2"), -Tokens.Number("DvSpace3"), 0);

        if (happening is { State: TimelineState.Live, Kind: TimelineKind.Tool })
        {
            layers.Children.Add(new TravelLine { IsRunning = true, Margin = lift });
        }
        else if (happening is { State: TimelineState.Live, Kind: TimelineKind.Step, Writing: false })
        {
            var breath = new Border { Height = Tokens.Number("DvFocusWidth"), VerticalAlignment = VerticalAlignment.Top, Margin = lift };
            breath.Loaded += (_, _) =>
            {
                breath.Background = Tokens.Brush(this, "DvIon");
                Motion.Breathe(breath);
            };
            breath.Unloaded += (_, _) => Motion.Rest(breath);
            layers.Children.Add(breath);
        }
    }

    protected override void Paint()
    {
        var frame = (Facet)Frame!;
        frame.Cut = Tokens.Number("DvCutS");
        frame.Fill = Tokens.Brush(this, isOpen || IsHovered ? "DvBg3" : "DvBg2");
        frame.Edge = Tokens.Brush(this, edge ?? "DvLine");
        frame.Ring = HasKeyboardFocus ? Tokens.Brush(this, "DvEmber") : null;
        Foreground = Tokens.Brush(this, "DvText");
        label.Foreground = Tokens.Brush(this, "DvText");
    }

    #endregion
}
