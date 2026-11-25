namespace Drover.Native.Pages;

/// <summary>Inspect, add, and test connections and replace their encrypted keys.</summary>
public sealed class ModelConnectionsPage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly ModelConnectionsViewModel model = Make<ModelConnectionsViewModel>();

    private readonly Sheet addSheet = new() { Title = "Add a model connection" };

    private readonly Dialog replaceDialog = new();

    private readonly SegmentedControl provider = new() { Label = "Provider", Options = ModelConnectionsViewModel.Providers };

    private readonly Field nameField;

    private readonly Field baseUrlField;

    private readonly Field keyField;

    private readonly Field newKeyField;

    private readonly StatusNote addProblem = new() { Kind = NoteKind.Danger };

    private readonly StatusNote addBusy = new() { Kind = NoteKind.Info };

    private readonly StatusNote replaceProblem = new() { Kind = NoteKind.Danger };

    private readonly StatusNote replaceBusy = new() { Kind = NoteKind.Info };

    private readonly Button cancelAdd;

    private readonly Button addButton;

    private readonly Button cancelReplace;

    private readonly Button saveKey;

    #endregion

    #region Constructors

    public ModelConnectionsPage()
    {
        nameField = PageParts.Field("Connection name", string.Empty, value => model.Name = value);
        baseUrlField = PageParts.Field("Base address", string.Empty, value => model.BaseUrl = value, "url",
            "Any server that speaks the chat completions API.");
        keyField = PageParts.Field("Key", string.Empty, value => model.ApiKey = value, "password", KeyHint());
        newKeyField = PageParts.Field("New key", string.Empty, value => model.NewKey = value, "password",
            "Stored encrypted. It is not shown again.");
        provider.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) =>
        {
            model.ChooseProvider(provider.Value ?? model.Kind);
            UpdateAddForm();
        });

        cancelAdd = PageParts.Action("Cancel", () => { model.Adding = false; return Task.CompletedTask; }, Refresh);
        addButton = PageParts.Action("Add and test", model.Add, Refresh, ButtonKind.Primary);
        addSheet.Body = SheetBody(provider, nameField, baseUrlField, keyField, addProblem, addBusy,
            PageParts.Row(cancelAdd, addButton));
        nameField.Loaded += (_, _) => { if (addSheet.Open) { nameField.TakeFocus(); } };
        addSheet.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (addSheet.Open)
            {
                addSheet.DispatcherQueue.TryEnqueue(() => nameField.TakeFocus());
            }
            else if (model.Adding)
            {
                model.Adding = false;
                Refresh();
            }
        });

        cancelReplace = PageParts.Action("Cancel", () => { model.CancelReplacing(); return Task.CompletedTask; }, Refresh);
        saveKey = PageParts.Action("Save key", () => model.ReplaceKey(Replacing()), Refresh, ButtonKind.Primary);
        replaceDialog.Body = SheetBody(newKeyField, replaceProblem, replaceBusy, PageParts.Row(cancelReplace, saveKey));
        newKeyField.Loaded += (_, _) => { if (replaceDialog.Open) { newKeyField.TakeFocus(); } };
        replaceDialog.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (replaceDialog.Open)
            {
                replaceDialog.DispatcherQueue.TryEnqueue(() => newKeyField.TakeFocus());
            }
            else if (model.Replacing is not null)
            {
                model.CancelReplacing();
            }
        });

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

        var header = new ScreenHeader { Title = "Model connections" };
        if (model.CanEdit && model.Connections is { Count: > 0 })
        {
            var add = new IconButton
            {
                Icon = IconName.Plus,
                Label = "Add a model connection",
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetAutomationId(add, "AddModelConnection");
            add.Click += (_, _) => { model.StartAdding(); Refresh(); };
            header.Actions.Add(add);
        }

        body.Add(header);
        body.Add(new StatusNote { Text = model.Notice, Kind = model.NoticeIsProblem ? NoteKind.Danger : NoteKind.Ok });

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
        else if (model.Connections.Count == 0)
        {
            body.Add(Empty());
        }
        else
        {
            foreach (var connection in model.Connections)
            {
                body.Add(Connection(connection));
            }
        }

        UpdateAddForm();
        UpdateReplaceForm();
        addSheet.Open = model.Adding;
        replaceDialog.Open = model.Replacing is not null;
        body.Add(addSheet);
        body.Add(replaceDialog);
    }

    private UIElement Empty()
    {
        if (!model.CanEdit)
        {
            return Styles.Text("This space has no model connections yet. An owner or admin of the space can add one.", "DvTextSecondary");
        }

        var empty = new EmptyState
        {
            Title = "No model connection yet",
            Cause = "Bots think with a model you bring. Add a connection, then choose it in a bot's Model section.",
            ActionLabel = "Add a model connection",
        };
        empty.Action += (_, _) => { model.StartAdding(); Refresh(); };
        return empty;
    }

    private void UpdateAddForm()
    {
        if (provider.Value != model.Kind)
        {
            provider.Value = model.Kind;
        }

        nameField.Value = model.Name;
        baseUrlField.Value = model.BaseUrl;
        keyField.Value = model.ApiKey;
        keyField.Hint = KeyHint();
        baseUrlField.Visibility = model.Kind is "openai_compatible" ? Visibility.Visible : Visibility.Collapsed;
        addProblem.Text = model.AddProblem;
        addBusy.Text = model.AddingBusy ? "Adding the connection." : string.Empty;
        addBusy.Visibility = model.AddingBusy ? Visibility.Visible : Visibility.Collapsed;
        cancelAdd.IsEnabled = !model.AddingBusy;
        addButton.IsEnabled = !model.AddingBusy;
    }

    private void UpdateReplaceForm()
    {
        if (model.Replacing is not { } id)
        {
            newKeyField.Value = string.Empty;
            return;
        }

        newKeyField.Value = model.NewKey;
        replaceProblem.Text = model.Problems.GetValueOrDefault(id);
        replaceBusy.Text = model.ChangePending ? "Saving the key." : string.Empty;
        replaceBusy.Visibility = model.ChangePending ? Visibility.Visible : Visibility.Collapsed;
        cancelReplace.IsEnabled = !model.ChangePending;
        saveKey.IsEnabled = !model.ChangePending;
    }

    private string KeyHint() => model.Kind is "openai_compatible"
        ? "Leave this empty if the server needs no key."
        : "Made in your Venice account, under API keys.";

    private static StackPanel SheetBody(params UIElement[] children)
    {
        var column = new StackPanel { Spacing = Tokens.Number("DvSpace4") };
        foreach (var child in children)
        {
            column.Children.Add(child);
        }

        return column;
    }

    private Drover.Contracts.Models.ModelConnectionSummary Replacing() =>
        model.Connections!.First(connection => connection.Id == model.Replacing);

    private ListCard Connection(Drover.Contracts.Models.ModelConnectionSummary connection)
    {
        var status = ModelConnectionsViewModel.StatusOf(connection);
        var card = new ListCard
        {
            Title = connection.Name,
            Facts = ModelConnectionsViewModel.Facts(connection),
            Status = new Badge { Text = status.Words, Kind = status.Kind },
            Trailing = CardMenu(connection),
        };

        var beneath = PageParts.Column();
        if (model.Problems.TryGetValue(connection.Id, out var problem))
        {
            beneath.Children.Add(new StatusNote { Kind = NoteKind.Danger, Text = problem });
        }

        if (model.ShownCatalog == connection.Id && model.Catalog is { } catalog)
        {
            if (catalog.Count == 0)
            {
                beneath.Children.Add(Styles.Text("This connection has no listed models. You can enter a model ID in the bot's Model section.", "DvTextSecondary"));
            }

            foreach (var entry in catalog)
            {
                beneath.Children.Add(CatalogRow(connection, entry));
            }
        }

        card.Beneath = beneath.Children.Count > 0 ? beneath : null;
        return card;
    }

    private Menu CardMenu(Drover.Contracts.Models.ModelConnectionSummary connection)
    {
        var menu = new Menu
        {
            Label = "More",
            AccessibleLabel = $"More for {connection.Name}",
            TriggerIcon = IconName.More,
            ShowTriggerLabel = false,
        };

        if (model.ShownCatalog == connection.Id)
        {
            menu.Items.Add(MenuEntry("Hide models", () => { model.HideCatalog(); Refresh(); }));
            menu.Items.Add(MenuEntry("Refresh models", () => Run(() => model.RefreshCatalog(connection))));
        }
        else
        {
            menu.Items.Add(MenuEntry("View models", () => Run(() => model.ShowCatalog(connection))));
        }

        menu.Items.Add(MenuEntry("Test connection", () => Run(() => model.Test(connection))));
        if (model.CanEdit)
        {
            menu.Items.Add(MenuEntry("Replace key", () => { model.StartReplacing(connection); Refresh(); }));
        }

        return menu;
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
        reads.OnChanged(() => model.ChangePending, on => Run(() => model.SetVision(connection, entry, on)));
        return PageParts.Column(line, reads);
    }

    private void Run(Func<Task> work)
    {
        var pending = work();
        Refresh();
        _ = Finish(pending);
    }

    private async Task Finish(Task pending)
    {
        try
        {
            await pending;
        }
        finally
        {
            Refresh();
        }
    }

    private static MenuItem MenuEntry(string label, Action choose)
    {
        var item = new MenuItem { Content = label, HorizontalContentAlignment = HorizontalAlignment.Left };
        item.Selected += (_, _) => choose();
        return item;
    }

    #endregion
}
