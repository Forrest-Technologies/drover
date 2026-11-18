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

    protected override async void Opened(RouteMatch<Type> route)
    {
        await model.Open();
        opening = false;
        Refresh();
    }

    private void Refresh()
    {
        var body = layout.Body;
        body.Clear();
        body.Add(PageParts.Title("Status"));
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
            body.Add(PageParts.Action("Check again", model.Load, Refresh, !model.Loading));
            if (report.ServerRowsShown)
            {
                body.Add(Group("This server", model.ServerRows));
            }

            body.Add(Group("This space", model.SpaceRows));
            if (model.SpaceRows.Count == 0)
            {
                body.Add(Styles.Text("No model or tool connection yet. Add a model connection or a tool server and it is listed here.", "DvTextSecondary"));
                body.Add(PageParts.Row(new Link { Text = "Add a model connection", Address = Addresses.SetupModel }, new Link { Text = "Add a tool server", Address = Addresses.Tools }));
            }
        }
    }

    private static StackPanel Group(string title, IReadOnlyList<Drover.Contracts.Status.StatusRow> rows)
    {
        var group = PageParts.Column(Styles.Text(title, "DvTextH2"));
        foreach (var row in rows)
        {
            var (kind, word) = row.State switch
            {
                "ok" => (BadgeKind.Ok, "Working"),
                "warning" => (BadgeKind.Warn, "Needs attention"),
                "problem" => (BadgeKind.Danger, "Not working"),
                _ => (BadgeKind.Neutral, "Off"),
            };
            var entry = PageParts.Column(PageParts.Row(Styles.Text(row.Name, "DvTextLabel"), new Badge { Kind = kind, Text = word }), Styles.Text(row.Summary));
            if (row.CheckedAt is { } at)
            {
                entry.Children.Add(Styles.Text($"checked {at.ToLocalTime():d MMM, HH:mm}", "DvTextMono"));
            }

            group.Children.Add(entry);
        }

        return group;
    }

    #endregion
}
