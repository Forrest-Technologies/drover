namespace Drover.Native.Chat;

/// <summary>
/// The work a bot did between two things it said, as one quiet list (<see cref="TurnLedger"/>) in one frame. Closed,
/// it is one line: what was done in order, then the number of steps, the time they took and any that failed. Opened,
/// it shows a single line row for each step, and a row opens to the call itself. While a call runs, the list and its
/// row open by themselves so its output can be watched, and close when it ends, unless the person opened or closed
/// them. Work of one step is that row alone, with no line above it. The arrow keys, Home, and End move between rows. Only running work moves: while a step runs,
/// the line travels around the frame.
/// </summary>
public sealed class WorkLedger : Grid
{
    #region Private Fields

    private readonly Facet frame = new();

    private readonly StackPanel column = new();

    private readonly PlainButton toggle = new() { HoverFill = "DvBg2", RingInside = true, Radius = null };

    private readonly TextBlock gist = Styles.Text(null, "DvTextLabel");

    private readonly TextBlock facts = Styles.Text(null, "DvTextCaption");

    private readonly Icon mark = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly Border divider = new();

    private readonly StackPanel rows = new();

    private readonly TravelLine travel = new();

    private readonly ArrowKeyZone zone = new();

    private IReadOnlyList<LedgerStep> steps = [];

    private bool open;

    // What the person chose by opening or closing the list. Until they choose, it is open while a call runs.
    private bool? chosen;

    #endregion

    #region Constructors

    public WorkLedger()
    {
        gist.TextTrimming = facts.TextTrimming = TextTrimming.CharacterEllipsis;
        gist.TextWrapping = facts.TextWrapping = TextWrapping.NoWrap;
        gist.VerticalAlignment = facts.VerticalAlignment = VerticalAlignment.Center;

        var line = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        SetColumn(facts, 1);
        SetColumn(mark, 2);
        line.Children.Add(gist);
        line.Children.Add(facts);
        line.Children.Add(mark);
        foreach (var part in new FrameworkElement[] { gist, facts, mark })
        {
            AutomationProperties.SetAccessibilityView(part, AccessibilityView.Raw);
        }

        toggle.Content = line;
        toggle.HorizontalAlignment = HorizontalAlignment.Stretch;
        toggle.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        toggle.Padding = Tokens.Space("DvSpace3", "DvSpace2");
        toggle.Click += (_, _) => Toggle();
        divider.Height = Tokens.Number("DvBorder");

        zone.Items = rows;
        column.Children.Add(toggle);
        column.Children.Add(divider);
        column.Children.Add(zone);
        frame.Children.Add(column);
        frame.Children.Add(travel);
        Children.Add(frame);
        Loaded += (_, _) => toggle.MinHeight = Targets.Minimum;
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    /// <summary>The parameters the conversation draws its tool calls with.</summary>
    public ChatEntryParameters? Parameters { get; set; }

    #endregion

    #region Public Methods

    /// <summary>Shows the work's steps. The list stays open or closed, and an open row stays open, as the work grows.</summary>
    public void Show(IReadOnlyList<LedgerStep> shown)
    {
        steps = shown;
        zone.Label = TurnLedger.ListName(shown);
        Keyed.Sync(rows, shown, step => step.Key, _ => new LedgerRow(), (row, step) =>
        {
            row.Parameters = Parameters;
            row.Show(step);
        });

        for (var index = 0; index < rows.Children.Count; index++)
        {
            ((LedgerRow)rows.Children[index]).Divided = index > 0;
        }

        Paint();
    }

    #endregion

    #region Private Methods

    // One step needs no line to sum it up: its row is the line.
    private bool Summed => steps.Count > 1;

    private void Toggle()
    {
        chosen = !open;
        Paint();
    }

    private void Paint()
    {
        var running = steps.Any(step => TurnLedger.StateOf(step) is LedgerState.Running);
        open = chosen ?? steps.Any(step => step.Reasoning is null && TurnLedger.StateOf(step) is LedgerState.Running);
        frame.Cut = travel.Cut = Tokens.Number("DvCut");
        frame.Fill = Tokens.Brush(this, "DvBg1");
        frame.Edge = Tokens.Brush(this, running ? "DvText3" : "DvLine");
        travel.Margin = TravelLine.Over(frame.Padding);
        travel.IsRunning = running;

        toggle.Visibility = Summed ? Visibility.Visible : Visibility.Collapsed;
        divider.Visibility = Summed && open ? Visibility.Visible : Visibility.Collapsed;
        zone.Visibility = !Summed || open ? Visibility.Visible : Visibility.Collapsed;
        divider.Background = Tokens.Brush(this, "DvLine");

        gist.Text = TurnLedger.Gist(steps);
        gist.Foreground = Tokens.Brush(this, "DvText");
        facts.Text = TurnLedger.Facts(steps);
        facts.Foreground = Tokens.Brush(this, running ? "DvIon" : "DvText3");
        mark.NameOfIcon = open ? IconName.ChevronDown : IconName.ChevronRight;
        mark.Foreground = Tokens.Brush(this, "DvText3");

        var name = $"{gist.Text}, {facts.Text.Replace(" · ", ", ", StringComparison.Ordinal)}";
        AutomationProperties.SetName(toggle, name);
        AutomationProperties.SetItemStatus(toggle, open ? "Expanded" : "Collapsed");
        ToolTipService.SetToolTip(toggle, name);
    }

    #endregion
}
