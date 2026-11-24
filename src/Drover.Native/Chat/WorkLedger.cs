namespace Drover.Native.Chat;

/// <summary>
/// A bot turn's work as one quiet list (<see cref="TurnLedger"/>): one frame and one line around it, a single line
/// row for each step, and no frame, badge, or accent of a row's own. Rows are closed until a person opens one, and the
/// arrow keys, Home, and End move between them. Only a running row moves: the line travels its top edge.
/// </summary>
public sealed class WorkLedger : Grid
{
    #region Private Fields

    private readonly Facet frame = new();

    private readonly StackPanel rows = new();

    private readonly ArrowKeyZone zone = new();

    private IReadOnlyList<LedgerStep> steps = [];

    #endregion

    #region Constructors

    public WorkLedger()
    {
        zone.Items = rows;
        frame.Children.Add(zone);
        Children.Add(frame);
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    /// <summary>The parameters the conversation draws its tool calls with.</summary>
    public ChatEntryParameters? Parameters { get; set; }

    #endregion

    #region Public Methods

    /// <summary>Shows the turn's steps. A row that was open stays open while the turn grows.</summary>
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

    private void Paint()
    {
        frame.Cut = Tokens.Number("DvCut");
        frame.Fill = Tokens.Brush(this, "DvBg1");
        frame.Edge = Tokens.Brush(this, steps.Any(step => TurnLedger.StateOf(step) is LedgerState.Running) ? "DvText3" : "DvLine");
    }

    #endregion
}
