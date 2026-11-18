namespace Drover.Native.Pages;

/// <summary>Pick a bot to change or make a new one at its place in the crew.</summary>
public sealed class BotListPage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly BotListViewModel model = Make<BotListViewModel>();

    #endregion

    #region Constructors

    public BotListPage()
    {
        Content = layout;
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => "Bots · Drover";

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route)
    {
        await model.Open();
        Refresh();
    }

    private void Refresh()
    {
        var body = layout.Body;
        body.Clear();
        body.Add(PageParts.Title("Bots"));
        body.Add(new StatusNote { Text = model.Problem, Kind = NoteKind.Danger });
        if (model.LoadProblem is { } problem)
        {
            var failed = new EmptyState { Title = "Drover could not load the bots", Cause = problem, ActionLabel = "Try again" };
            failed.Action += async (_, _) => { await model.Load(); Refresh(); };
            body.Add(failed);
        }
        else if (model.Bots is null || model.Connections is null)
        {
            body.Add(PageParts.Waiting("Loading the bots."));
        }
        else if (model.Connections.Count == 0)
        {
            body.Add(new EmptyState { Title = "No model is connected", Cause = "A bot needs a model to think with, and this space has none yet.", ActionLabel = "Connect a model", ActionHref = Addresses.SetupModel });
        }
        else
        {
            ShowBots();
        }
    }

    private void ShowBots()
    {
        if (model.CanEdit)
        {
            if (model.Bots!.Count == 0)
            {
                layout.Body.Add(Styles.Text("No bot yet", "DvTextH2"));
                layout.Body.Add(Styles.Text("Make one, then give it a name, a purpose, and its tools.", "DvTextSecondary"));
            }

            layout.Body.Add(PageParts.Action(model.Creating ? "Making a bot" : "New bot", () => model.Create(null), Refresh, !model.Creating, ButtonKind.Primary));
        }
        else
        {
            layout.Body.Add(Styles.Text(model.Bots!.Count == 0
                ? "This space has no bots yet. An owner or admin of the space can make one."
                : "Owners and admins of this space change its bots. You can see them and talk to them.", "DvTextSecondary"));
        }

        foreach (var node in CrewTree.From(model.Bots!))
        {
            layout.Body.Add(Row(node));
        }
    }

    private StackPanel Row(CrewNode node)
    {
        var bot = node.Bot;
        var identity = PageParts.Column(
            new Link { Text = bot.Name, Address = model.CanEdit ? Addresses.Bot(bot.Id) : Addresses.Talk(bot.Id) },
            Styles.Text($"@{bot.Handle} · {bot.Model}{model.MadeBy(bot)}", "DvTextMono"));
        var row = PageParts.Row(new Avatar { BotName = bot.Name, Hue = BotIdentity.Hue(bot.Id), Size = (int)Tokens.Number("DvAvatar48") }, identity);
        if (bot.Status is "paused")
        {
            row.Children.Add(new Badge { Text = "Paused", Kind = BadgeKind.Warn });
        }

        if (model.CanEdit)
        {
            row.Children.Add(PageParts.Action($"New bot under {bot.Name}", () => model.Create(bot.Id), Refresh, !model.Creating));
        }

        var result = PageParts.Column(PageParts.Card(row));
        foreach (var child in node.Children)
        {
            var nested = Row(child);
            nested.Margin = new(Tokens.Number("DvSpace4"), 0, 0, 0);
            result.Children.Add(nested);
        }

        return result;
    }

    #endregion
}
