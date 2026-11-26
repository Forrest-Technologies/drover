namespace Drover.Native.Pages;

/// <summary>
/// One tool in the Tools section: what it is and what it may do, why it is held back, its grant for this bot, and
/// when its server changed it, what was reviewed and a way to accept the change.
/// </summary>
public sealed class ToolRowView : Border
{
    #region Private Fields

    private readonly ToolsSectionViewModel part;

    private readonly TextBlock title = Styles.Text(null);

    private readonly TextBlock facts = BotParts.Mono();

    private readonly TextBlock description = BotParts.Hint();

    private readonly TextBlock missing = Warning();

    private readonly TextBlock alwaysAsks = Warning(ToolsSectionViewModel.AlwaysAsks);

    private readonly TextBlock limit = Warning();

    private readonly SegmentedControl grant = new();

    private readonly StackPanel review;

    private readonly TextBlock reviewWords = BotParts.Hint();

    private readonly OpenableGroup reviewed = new(open: false, ruled: false);

    private readonly TextBlock reviewedWords = BotParts.Hint();

    private readonly TextBlock nowWords = BotParts.Hint();

    private readonly TextBlock sameWords = BotParts.Hint(ToolsSectionViewModel.SameWords);

    private readonly Button accept;

    private BotToolEntry tool;

    private bool showing;

    #endregion

    #region Constructors

    public ToolRowView(ToolsSectionViewModel part, BotToolEntry tool)
    {
        (this.part, this.tool) = (part, tool);
        title.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        var text = BotParts.Column("DvSpace1", title, facts, description, missing, alwaysAsks, limit);
        grant.OnChanged(() => showing, value => _ = part.Set(this.tool, value));
        accept = BotParts.Action(ToolsSectionViewModel.Accept, () =>
        {
            if (this.tool.ConnectionId is { } connectionId)
            {
                _ = part.AcceptChange(this.tool, connectionId);
            }
        });
        reviewed.Show(ToolsSectionViewModel.LastReviewed, null);
        reviewed.Body.Add(Compare());
        reviewed.Body.Add(sameWords);
        reviewed.HorizontalAlignment = HorizontalAlignment.Stretch;
        review = BotParts.Column("DvSpace2", new Badge { Text = ToolsSectionViewModel.NeedsReview, Kind = BadgeKind.Warn }, reviewWords, reviewed, accept);
        var row = new WrapRow();
        row.Children.Add(text);
        row.Children.Add(grant);
        row.Children.Add(review);
        Child = row;
        Padding = new(0, Tokens.Number("DvSpace3"), 0, Tokens.Number("DvSpace3"));
        BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0);
        this.WhenShownOrRethemed(() => BorderBrush = Tokens.Brush(this, "DvLine"));
    }

    #endregion

    #region Public Methods

    public void Show(BotToolEntry entry, ToolsSectionViewModel.ToolGroup group)
    {
        showing = true;
        tool = entry;
        title.Show(entry.Title);
        facts.Show(ToolsSectionViewModel.Facts(entry));
        description.Shown(entry.Description.Length > 0).Show(entry.Description);
        missing.Shown(entry.State is "missing").Show(ToolsSectionViewModel.NoLongerListed(group));
        alwaysAsks.Shown(entry.AlwaysAsks);
        limit.Shown(ToolsSectionViewModel.Limit(entry) is not null).Show(ToolsSectionViewModel.Limit(entry));
        grant.Label = part.GrantLabel(entry);
        grant.Show(ToolsSectionViewModel.OptionsFor(entry), entry.Grant);
        grant.Disabled = part.Busy == entry.ToolId || part.BusyGroup == group.Name;
        review.Shown(entry.NeedsReview);
        reviewWords.Show(part.ReviewWords);
        reviewed.Shown(entry.Reviewed is not null);

        if (entry.Reviewed is { } before)
        {
            reviewedWords.Show(ToolsSectionViewModel.ReviewedWords(entry, before));
            nowWords.Show(ToolsSectionViewModel.NowWords(entry));
            sameWords.Shown(ToolsSectionViewModel.WordsAreSame(entry, before));
        }

        accept.Shown(entry.ConnectionId is not null).IsEnabled = part.Busy != entry.ToolId;
        showing = false;
    }

    #endregion

    #region Private Methods

    private static TextBlock Warning(string? text = null) => BotParts.Small(text, "DvWarn");

    // What was last reviewed beside what the tool says now, each under its name.
    private Grid Compare()
    {
        var grid = new Grid { ColumnSpacing = Tokens.Number("DvSpace3"), RowSpacing = Tokens.Number("DvSpace2") };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var reviewedTerm = BotParts.Small(ToolsSectionViewModel.ReviewedTerm, "DvText3");
        var nowTerm = BotParts.Small(ToolsSectionViewModel.NowTerm, "DvText3");
        Grid.SetRow(nowTerm, 1);
        Grid.SetColumn(reviewedWords, 1);
        Grid.SetRow(nowWords, 1);
        Grid.SetColumn(nowWords, 1);
        grid.Children.Add(reviewedTerm);
        grid.Children.Add(reviewedWords);
        grid.Children.Add(nowTerm);
        grid.Children.Add(nowWords);
        return grid;
    }

    #endregion
}
