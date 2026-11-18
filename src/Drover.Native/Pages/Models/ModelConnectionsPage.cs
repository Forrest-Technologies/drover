namespace Drover.Native.Pages;

/// <summary>Inspect, add, and test model connections and replace their encrypted keys.</summary>
public sealed class ModelConnectionsPage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly ModelConnectionsViewModel model = Make<ModelConnectionsViewModel>();

    #endregion

    #region Constructors

    public ModelConnectionsPage()
    {
        Content = layout;
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => "Model connections · Drover";

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
        body.Add(PageParts.Title("Model connections"));
        body.Add(new StatusNote { Text = model.Notice, Kind = model.NoticeIsProblem ? NoteKind.Danger : NoteKind.Ok });
        if (model.CanEdit && !model.Adding && model.Connections is { Count: > 0 })
        {
            body.Add(Action("Add a model connection", () => { model.StartAdding(); return Task.CompletedTask; }, ButtonKind.Primary));
        }

        if (model.Adding)
        {
            body.Add(AddForm());
        }

        if (model.LoadProblem is { } problem)
        {
            var failed = new EmptyState { Title = "Drover could not load the model connections", Cause = problem, ActionLabel = "Try again" };
            failed.Action += async (_, _) => { await model.Load(); Refresh(); };
            body.Add(failed);
        }
        else if (model.Connections is null)
        {
            body.Add(PageParts.Waiting("Loading the model connections."));
        }
        else if (model.Connections.Count == 0 && !model.Adding)
        {
            body.Add(Styles.Text(model.CanEdit ? "No model connection yet" : "This space has no model connections yet. An owner or admin of the space can add one.", "DvTextSecondary"));
            if (model.CanEdit)
            {
                body.Add(Styles.Text("Bots think with a model you bring. Add a connection, then choose it in a bot's Model section.", "DvTextSecondary"));
                body.Add(Action("Add a model connection", () => { model.StartAdding(); return Task.CompletedTask; }, ButtonKind.Primary));
            }
        }
        else
        {
            foreach (var connection in model.Connections)
            {
                body.Add(Connection(connection));
            }
        }
    }

    private Facet AddForm()
    {
        var provider = new SegmentedControl { Label = "Provider", Options = ModelConnectionsViewModel.Providers, Value = model.Kind, Disabled = model.AddingBusy };
        provider.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) => { model.ChooseProvider(provider.Value ?? model.Kind); Refresh(); });
        var fields = PageParts.Column(
            Styles.Text("Add a model connection", "DvTextH2"), provider,
            PageParts.Field("Connection name", model.Name, value => model.Name = value, disabled: model.AddingBusy));
        if (model.Kind is "openai_compatible")
        {
            fields.Children.Add(PageParts.Field("Base address", model.BaseUrl, value => model.BaseUrl = value, "url", "Any server that speaks the chat completions API.", model.AddingBusy));
        }

        fields.Children.Add(PageParts.Field("Key", model.ApiKey, value => model.ApiKey = value, "password", model.Kind is "openai_compatible"
            ? "Leave this empty if the server needs no key." : "Made in your Venice account, under API keys.", model.AddingBusy));
        fields.Children.Add(new StatusNote { Text = model.AddProblem, Kind = NoteKind.Danger });
        fields.Children.Add(PageParts.Row(
            Action("Cancel", () => { model.Adding = false; return Task.CompletedTask; }),
            Action(model.AddingBusy ? "Adding" : "Add and test", model.Add, ButtonKind.Primary)));
        return PageParts.Card(fields);
    }

    private Facet Connection(Drover.Contracts.Models.ModelConnectionSummary connection)
    {
        var status = ModelConnectionsViewModel.StatusOf(connection);
        var body = PageParts.Column(
            PageParts.Row(Styles.Text(connection.Name, "DvTextH2"), new Badge { Text = status.Words, Kind = status.Kind }),
            Styles.Text(ModelConnectionsViewModel.Facts(connection), "DvTextMono"));
        if (model.Problems.TryGetValue(connection.Id, out var problem))
        {
            body.Children.Add(new StatusNote { Kind = NoteKind.Danger, Text = problem });
        }

        var actions = PageParts.Row(Action("View models", () => model.ShowCatalog(connection)));
        if (model.CanEdit && model.ShownCatalog == connection.Id)
        {
            actions.Children.Add(Action("Refresh models", () => model.RefreshCatalog(connection)));
        }

        body.Children.Add(actions);
        if (model.ShownCatalog == connection.Id && model.Catalog is { } catalog)
        {
            if (catalog.Count == 0)
            {
                body.Children.Add(Styles.Text("This connection has no listed models. You can enter a model ID in the bot’s Model section.", "DvTextSecondary"));
            }

            foreach (var entry in catalog)
            {
                body.Children.Add(Styles.Text($"{entry.DisplayName} · {entry.Model}{(entry.State is "available" ? string.Empty : $" ({entry.State})")}"));
            }
        }

        if (model.CanEdit)
        {
            body.Children.Add(Action("Test connection", () => model.Test(connection)));
            if (model.Replacing == connection.Id)
            {
                body.Children.Add(PageParts.Field("New key", model.NewKey, value => model.NewKey = value, "password", "Stored encrypted. It is not shown again.", model.ChangePending));
                body.Children.Add(PageParts.Row(
                    Action("Cancel", () => { model.CancelReplacing(); return Task.CompletedTask; }),
                    Action("Save key", () => model.ReplaceKey(connection), ButtonKind.Primary)));
            }
            else
            {
                body.Children.Add(Action("Replace key", () => { model.StartReplacing(connection); return Task.CompletedTask; }));
            }
        }

        return PageParts.Card(body);
    }

    private Button Action(string label, Func<Task> work, ButtonKind kind = ButtonKind.Quiet) =>
        PageParts.Action(label, work, Refresh, !model.ChangePending, kind);

    #endregion
}
