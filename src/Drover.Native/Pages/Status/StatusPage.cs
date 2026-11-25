namespace Drover.Native.Pages;

/// <summary>See which server or space connection needs attention and what to do about it.</summary>
public sealed class StatusPage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly StatusPageViewModel model = Make<StatusPageViewModel>();

    private bool opening = true;

    #endregion

    #region Constructors

    public StatusPage()
    {
        Content = layout;
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => "Status · Drover";

    #endregion

    #region Private Methods

    protected override async Task Opened(RouteMatch<Type> route)
    {
        await model.Open();
        opening = false;
        Refresh();
    }

    private void Refresh()
    {
        var body = layout.Body;
        body.Clear();
        body.Add(new ScreenHeader { Title = "Status" });
        if (!opening && model.Report is not null)
        {
            body.Add(PageParts.Action("Check again", model.Load, Refresh, !model.Loading));
        }
        if (opening)
        {
            body.Add(PageParts.Waiting("Checking the server and the space's connections."));
        }
        else if (!model.CanSee)
        {
            body.Add(Styles.Text("An owner or admin of the space can see its status.", "DvTextSecondary"));
        }
        else if (model.LoadProblem is { } problem)
        {
            var failed = new EmptyState { Title = "Drover could not load the status", Cause = problem, ActionLabel = "Try again" };
            failed.Action += async (_, _) => { await model.Load(); Refresh(); };
            body.Add(failed);
        }
        else if (model.Report is not { } report)
        {
            body.Add(PageParts.Waiting("Checking the server and the space's connections."));
        }
        else
        {
            if (report.ServerRowsShown)
            {
                body.Add(Group("This server", model.ServerRows));
            }

            var space = Group("This space", model.SpaceRows);
            if (model.SpaceRows.Count == 0)
            {
                space.Children.Add(Styles.Text("No model or tool connection yet. Add a model connection or a tool server and it is listed here.", "DvTextSecondary"));
                space.Children.Add(PageParts.Row(new Link { Text = "Add a model connection", Address = Addresses.SetupModel }, new Link { Text = "Add a tool server", Address = Addresses.Tools }));
            }

            body.Add(space);
        }
    }

    // One list per owner of the problem: a small heading, then each row under a line, its name and badge on one line.
    private static StackPanel Group(string title, List<Drover.Contracts.Status.StatusRow> rows)
    {
        var heading = Styles.Text(title, "DvTextH3");
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        var group = PageParts.Column(heading);
        var list = new StackPanel();
        foreach (var row in rows)
        {
            list.Children.Add(Line(row));
        }

        if (rows.Count > 0)
        {
            group.Children.Add(list);
        }

        return group;
    }

    private static Border Line(Drover.Contracts.Status.StatusRow row)
    {
        var (kind, word) = row.State switch
        {
            "ok" => (BadgeKind.Ok, "Working"),
            "warning" => (BadgeKind.Warn, "Needs attention"),
            "problem" => (BadgeKind.Danger, "Not working"),
            _ => (BadgeKind.Neutral, "Off"),
        };
        var name = Styles.Text(row.Name);
        name.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        var head = new FlexPanel { Gap = Tokens.Number("DvSpace3"), RowGap = Tokens.Number("DvSpace2"), Justify = FlexJustify.SpaceBetween };
        head.Children.Add(name);
        head.Children.Add(new Badge { Kind = kind, Text = word });
        var entry = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
        entry.Children.Add(head);
        entry.Children.Add(Styles.Text(row.Summary, "DvTextSecondary"));
        if (row.CheckedAt is { } at)
        {
            entry.Children.Add(Styles.Text($"checked {at.ToLocalTime():d MMM, HH:mm}", "DvTextMono"));
        }

        var line = new Border
        {
            Child = entry,
            Padding = new(0, Tokens.Number("DvSpace3"), 0, Tokens.Number("DvSpace3")),
            BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0),
        };
        line.WhenShownOrRethemed(() => line.BorderBrush = Tokens.Brush(line, "DvLine"));
        return line;
    }

    #endregion
}
