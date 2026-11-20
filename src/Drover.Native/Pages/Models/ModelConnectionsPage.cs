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

    protected override async Task Opened(RouteMatch<Type> route)
    {
        await model.Open();
        Refresh();
    }

    private void Refresh()
    {
        var body = layout.Body;
        body.Clear();
        var header = new FlexPanel { Gap = Tokens.Number("DvSpace3"), Justify = FlexJustify.SpaceBetween };
        header.Children.Add(PageParts.Title("Model connections"));
        if (model.CanEdit && !model.Adding && model.Connections is { Count: > 0 })
        {
            var add = Action("Add a model connection", () => { model.StartAdding(); return Task.CompletedTask; }, ButtonKind.Primary);
            add.Content = PageParts.WithIcon(IconName.Plus, "Add a model connection");
            header.Children.Add(add);
        }

        body.Add(header);
        body.Add(new StatusNote { Text = model.Notice, Kind = model.NoticeIsProblem ? NoteKind.Danger : NoteKind.Ok });

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
            if (model.CanEdit)
            {
                var empty = new EmptyState
                {
                    Title = "No model connection yet",
                    Cause = "Bots think with a model you bring. Add a connection, then choose it in a bot's Model section.",
                    ActionLabel = "Add a model connection",
                };
                empty.Action += (_, _) => { model.StartAdding(); Refresh(); };
                body.Add(empty);
            }
            else
            {
                body.Add(Styles.Text("This space has no model connections yet. An owner or admin of the space can add one.", "DvTextSecondary"));
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
            PageParts.CardTitle("Add a model connection"), provider,
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
        var head = new FlexPanel { Gap = Tokens.Number("DvSpace3"), Justify = FlexJustify.SpaceBetween, CenterItems = false };
        head.Children.Add(PageParts.Column(PageParts.CardTitle(connection.Name), Styles.Text(ModelConnectionsViewModel.Facts(connection), "DvTextMono")));
        head.Children.Add(new Badge { Text = status.Words, Kind = status.Kind });
        var body = PageParts.Column(head);
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
                body.Children.Add(CatalogRow(connection, entry));
            }
        }

        if (model.CanEdit)
        {
            var keys = PageParts.Row(Action("Test connection", () => model.Test(connection)));
            if (model.Replacing == connection.Id)
            {
                keys.Children.Add(PageParts.Field("New key", model.NewKey, value => model.NewKey = value, "password", "Stored encrypted. It is not shown again.", model.ChangePending));
                keys.Children.Add(Action("Cancel", () => { model.CancelReplacing(); return Task.CompletedTask; }));
                keys.Children.Add(Action("Save key", () => model.ReplaceKey(connection), ButtonKind.Primary));
            }
            else
            {
                keys.Children.Add(Action("Replace key", () => { model.StartReplacing(connection); return Task.CompletedTask; }));
            }

            body.Children.Add(keys);
        }

        return PageParts.Card(body);
    }

    private UIElement CatalogRow(Drover.Contracts.Models.ModelConnectionSummary connection, Drover.Contracts.Models.ModelCatalogResponse entry)
    {
        var state = entry.State is "available" ? string.Empty : $" ({entry.State})";
        var words = ModelConnectionsViewModel.VisionWords(entry) is { } vision ? $" · {vision}" : string.Empty;
        var line = Styles.Text($"{entry.DisplayName} · {entry.Model}{state}{(model.CanMarkVision(connection) ? string.Empty : words)}");

        if (!model.CanMarkVision(connection))
        {
            return line;
        }

        var reads = new Switch { Label = ModelConnectionsViewModel.VisionLabel(entry), Value = entry.Capabilities.Vision, IsEnabled = !model.ChangePending };
        reads.OnChanged(() => model.ChangePending, on => SetVision(connection, entry, on));
        return PageParts.Column(line, reads);
    }

    private async void SetVision(Drover.Contracts.Models.ModelConnectionSummary connection, Drover.Contracts.Models.ModelCatalogResponse entry, bool readsImages)
    {
        var pending = model.SetVision(connection, entry, readsImages);
        Refresh();
        await pending;
        Refresh();
    }

    private Button Action(string label, Func<Task> work, ButtonKind kind = ButtonKind.Quiet) =>
        PageParts.Action(label, work, Refresh, !model.ChangePending, kind);

    #endregion
}
