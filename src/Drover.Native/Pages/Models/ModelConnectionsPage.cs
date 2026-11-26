namespace Drover.Native.Pages;

/// <summary>Inspect, add, and test connections and replace their encrypted keys.</summary>
public sealed class ModelConnectionsPage : Screen
{
    #region Private Fields

    private static readonly ILogger logger = Log.For<ModelConnectionsPage>();

    private readonly WorkLayout layout = new();

    private readonly StackPanel headerContent = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly ModelConnectionsViewModel model = Make<ModelConnectionsViewModel>();

    private readonly Sheet addSheet = new() { Title = "Add a model connection" };

    private readonly Dialog replaceDialog = new();

    private readonly Select provider = new() { Label = "Provider" };

    private readonly SegmentedControl providerSegments = new() { Label = "Provider", Fills = true };

    private readonly TextBlock onlyKind = Styles.Text(string.Empty, "DvTextSecondary");

    private readonly TextBlock providersLoading = PageParts.Waiting(NamedProviders.LoadingWords);

    private readonly EmptyState providersFailed = new() { Title = NamedProviders.FailedTitle, Cause = NamedProviders.FailedCause, ActionLabel = NamedProviders.RetryWords };

    private readonly TextBlock reach = Styles.Text(NamedProviders.ReachWords, "DvTextSecondary");

    private readonly Link keyLink = new();

    private readonly Field nameField;

    private readonly Field baseUrlField;

    private readonly Field keyField;

    private readonly Field newKeyField;

    private readonly StatusNote addProblem = new() { Kind = NoteKind.Danger };

    private readonly StatusNote addBusy = new() { Kind = NoteKind.Info };

    private readonly StatusNote replaceProblem = new() { Kind = NoteKind.Danger };

    private readonly StatusNote replaceBusy = new() { Kind = NoteKind.Info };

    private readonly Button changeAddress;

    private readonly Button cancelAdd;

    private readonly Button addButton;

    private readonly Button cancelReplace;

    private readonly Button saveKey;

    #endregion

    #region Constructors

    public ModelConnectionsPage()
    {
        nameField = PageParts.Field("Connection name", string.Empty, value => model.Name = value);
        baseUrlField = PageParts.Field("Base address", string.Empty, value => model.BaseUrl = value, "url");
        keyField = PageParts.Field("Key", string.Empty, value => model.ApiKey = value, "password", model.Named.KeyHint);
        newKeyField = PageParts.Field("New key", string.Empty, value => model.NewKey = value, "password",
            "Stored encrypted. It is not shown again.");
        provider.RegisterPropertyChangedCallback(Select.ValueProperty, (_, _) => ChooseProvider(provider.Value));
        providerSegments.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) => ChooseProvider(providerSegments.Value));
        providersFailed.Action += async (_, _) => { await model.RetryProviders(); UpdateAddForm(); };
        changeAddress = PageParts.Action(NamedProviders.ChangeAddressWords, () => { model.ChangeAddress(); UpdateAddForm(); return Task.CompletedTask; }, UpdateAddForm);
        nameField.Submitted += (_, _) => Run(model.Add);
        baseUrlField.Submitted += (_, _) => Run(model.Add);
        keyField.Submitted += (_, _) => Run(model.Add);
        newKeyField.Submitted += (_, _) => Run(ReplaceKey);

        cancelAdd = PageParts.Action("Cancel", CancelAdd, Refresh);
        addButton = PageParts.Action("Add connection", model.Add, Refresh, kind: ButtonKind.Primary);
        addSheet.Body = PageParts.SheetBody(onlyKind, providerSegments, provider, providersLoading, providersFailed, nameField, reach, baseUrlField, changeAddress, keyField, keyLink, addProblem, addBusy);
        addSheet.Actions = PageParts.Row(cancelAdd, addButton);
        nameField.Loaded += (_, _) => { if (addSheet.Open) { nameField.TakeFocus(); } };
        addSheet.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (addSheet.Open)
            {
                addSheet.DispatcherQueue.TryEnqueue(() => nameField.TakeFocus());
            }
            else
            {
                CloseAdd();
            }
        });

        cancelReplace = PageParts.Action("Cancel", CancelReplace, Refresh);
        saveKey = PageParts.Action("Save key", ReplaceKey, Refresh, kind: ButtonKind.Primary);
        replaceDialog.Body = PageParts.SheetBody(newKeyField, replaceProblem, replaceBusy);
        replaceDialog.Actions = PageParts.Row(cancelReplace, saveKey);
        newKeyField.Loaded += (_, _) => { if (replaceDialog.Open) { newKeyField.TakeFocus(); } };
        replaceDialog.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (replaceDialog.Open)
            {
                replaceDialog.DispatcherQueue.TryEnqueue(() => newKeyField.TakeFocus());
            }
            else
            {
                CloseReplace();
            }
        });

        layout.Body.Add(headerContent);
        layout.Body.Add(addSheet);
        layout.Body.Add(replaceDialog);
        Unloaded += (_, _) => CloseAll();
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
        headerContent.Children.Clear();

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
            add.Click += (_, _) => OpenAddSheet();
            header.Actions.Add(add);
        }

        headerContent.Children.Add(header);
        headerContent.Children.Add(new StatusNote
        {
            Text = model.Notice,
            Kind = model.NoticeIsProblem ? NoteKind.Danger : NoteKind.Ok,
        });

        if (model.LoadProblem is { } problem)
        {
            var failed = new EmptyState
            {
                Title = "Drover could not load the model connections",
                Cause = problem,
                ActionLabel = "Try again",
            };
            failed.Action += async (_, _) => { await model.Load(); Refresh(); };
            headerContent.Children.Add(failed);
        }
        else if (model.Connections is null)
        {
            headerContent.Children.Add(PageParts.Waiting("Loading the model connections."));
        }
        else if (model.Connections.Count == 0)
        {
            headerContent.Children.Add(Empty());
        }
        else
        {
            foreach (var connection in model.Connections)
            {
                headerContent.Children.Add(Connection(connection));
            }
        }

        UpdateAddForm();
        UpdateReplaceForm();
        addSheet.Open = model.Adding;
        replaceDialog.Open = model.Replacing is not null;
    }

    private UIElement Empty()
    {
        if (!model.CanEdit)
        {
            return Styles.Text(
                "This space has no model connections yet. An owner or admin of the space can add one.",
                "DvTextSecondary");
        }

        var empty = new EmptyState
        {
            Title = "No model connection yet",
            Cause = "A bot needs a model. Add a connection, then choose it in a bot's Model section.",
            ActionLabel = "Add a model connection",
        };
        empty.Action += (_, _) => OpenAddSheet();
        return empty;
    }

    private void OpenAddSheet()
    {
        model.StartAdding();
        UpdateAddForm();
        addSheet.Open = true;
        addSheet.DispatcherQueue.TryEnqueue(() => nameField.TakeFocus());
    }

    private Task CancelAdd()
    {
        CloseAdd();
        return Task.CompletedTask;
    }

    private void CloseAdd()
    {
        if (model.Adding || model.ApiKey.Length > 0)
        {
            model.CancelAdding();
        }

        UpdateAddForm();
    }

    private Task CancelReplace()
    {
        CloseReplace();
        return Task.CompletedTask;
    }

    private void CloseReplace()
    {
        if (model.Replacing is not null)
        {
            model.CancelReplacing();
        }

        UpdateReplaceForm();
    }

    private void CloseAll()
    {
        model.CancelAdding();
        model.CancelReplacing();
        UpdateAddForm();
        UpdateReplaceForm();
    }

    private void UpdateAddForm()
    {
        var busy = model.AddingBusy;
        var providers = model.Named;
        IReadOnlyList<SelectOption> options = [.. providers.Options.Select(option => new SelectOption(option.Value, option.Label))];
        if (!provider.Options.SequenceEqual(options))
        {
            provider.Options = options;
        }

        if (provider.Value != providers.Choice)
        {
            provider.Value = providers.Choice;
        }

        if (!providerSegments.Options.SequenceEqual(providers.Options))
        {
            providerSegments.Options = providers.Options;
        }

        if (providerSegments.Value != providers.Choice)
        {
            providerSegments.Value = providers.Choice;
        }

        provider.Disabled = providerSegments.Disabled = busy;
        provider.Visibility = providers.ShowsChoice && providers.UsesList ? Visibility.Visible : Visibility.Collapsed;
        providerSegments.Visibility = providers.ShowsChoice && !providers.UsesList ? Visibility.Visible : Visibility.Collapsed;
        onlyKind.Text = providers.OnlyKindWords ?? string.Empty;
        onlyKind.Visibility = providers.OnlyKindWords is null ? Visibility.Collapsed : Visibility.Visible;
        baseUrlField.Hint = providers.AddressHint;
        baseUrlField.Error = model.AddressProblem;
        keyField.Error = model.AddKeyProblem;
        providersLoading.Visibility = providers.ShowsLoading ? Visibility.Visible : Visibility.Collapsed;
        providersFailed.Cause = providers.FailureWords ?? NamedProviders.FailedCause;
        providersFailed.Visibility = providers.ShowsFailure ? Visibility.Visible : Visibility.Collapsed;
        var form = providers.ShowsForm ? Visibility.Visible : Visibility.Collapsed;
        nameField.Visibility = form;
        reach.Text = providers.ReachNote ?? string.Empty;
        reach.Visibility = providers.ReachNote is null ? Visibility.Collapsed : Visibility.Visible;
        changeAddress.Visibility = providers.OffersAddressChange ? Visibility.Visible : Visibility.Collapsed;
        keyField.Label = providers.KeyLabel;
        keyField.Visibility = providers.ShowsKey ? Visibility.Visible : Visibility.Collapsed;
        keyLink.Text = providers.KeyLinkText ?? string.Empty;
        keyLink.Address = providers.KeyLinkAddress;
        keyLink.Visibility = providers.KeyLinkText is null || !providers.ShowsKey ? Visibility.Collapsed : Visibility.Visible;
        nameField.Value = model.Name;
        nameField.Disabled = busy;
        baseUrlField.Value = model.BaseUrl;
        baseUrlField.Disabled = busy;
        keyField.Value = model.ApiKey;
        keyField.Disabled = busy;
        keyField.Hint = providers.KeyHint;
        baseUrlField.Visibility = providers.ShowsAddress ? Visibility.Visible : Visibility.Collapsed;
        addProblem.Text = model.AddProblem;
        addBusy.Text = busy ? "Adding the connection." : string.Empty;
        addBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        cancelAdd.IsEnabled = !busy;
        addButton.IsEnabled = providers.ShowsForm && !busy;
    }

    private void UpdateReplaceForm()
    {
        var busy = model.ChangePending;
        newKeyField.Value = model.Replacing is null ? string.Empty : model.NewKey;
        newKeyField.Disabled = busy;
        replaceProblem.Text = model.KeyProblem;
        replaceBusy.Text = busy ? "Saving the key." : string.Empty;
        replaceBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        cancelReplace.IsEnabled = !busy;
        saveKey.IsEnabled = !busy && model.Replacing is not null;
    }

    private void ChooseProvider(string? chosen)
    {
        if (chosen is { Length: > 0 } && chosen != model.Named.Choice)
        {
            model.ChooseProvider(chosen);
            UpdateAddForm();
        }
    }

    private Drover.Contracts.Models.ModelConnectionSummary? Replacing() =>
        model.Replacing is { } id ? model.Connections?.FirstOrDefault(connection => connection.Id == id) : null;

    private Task ReplaceKey()
    {
        var connection = Replacing();
        return connection is null ? Task.CompletedTask : model.ReplaceKey(connection);
    }

    private void OpenReplace(Drover.Contracts.Models.ModelConnectionSummary connection)
    {
        model.StartReplacing(connection);
        replaceDialog.Title = $"Replace the key for {connection.Name}";
        UpdateReplaceForm();
        replaceDialog.Open = true;
        replaceDialog.DispatcherQueue.TryEnqueue(() => newKeyField.TakeFocus());
    }

    private ListCard Connection(Drover.Contracts.Models.ModelConnectionSummary connection)
    {
        var status = model.StatusOf(connection);
        var card = new ListCard
        {
            Title = connection.Name,
            Facts = model.Facts(connection),
            Status = new Badge { Text = status.Words, Kind = status.Kind },
            Trailing = CardMenu(connection),
        };

        var beneath = PageParts.Column();
        if (model.OffWords(connection) is { } off)
        {
            beneath.Children.Add(Styles.Text(off, "DvTextSecondary"));
        }

        if (model.Problems.TryGetValue(connection.Id, out var problem))
        {
            beneath.Children.Add(new StatusNote { Kind = NoteKind.Danger, Text = problem });
        }

        if (model.ShownCatalog == connection.Id && model.Catalog is { } catalog)
        {
            if (catalog.Count == 0)
            {
                beneath.Children.Add(Styles.Text(
                    "This connection has no listed models. You can enter a model ID in the bot's Model section.",
                    "DvTextSecondary"));
            }

            foreach (var entry in catalog)
            {
                beneath.Children.Add(CatalogRow(connection, entry));
            }
        }

        card.Beneath = beneath.Children.Count > 0 ? beneath : null;
        return card;
    }

    private Menu? CardMenu(Drover.Contracts.Models.ModelConnectionSummary connection)
    {
        var menu = new Menu
        {
            Label = "More",
            AccessibleLabel = $"More for {connection.Name}",
            TriggerIcon = IconName.More,
            ShowTriggerLabel = false,
        };
        var enabled = !model.ChangePending;
        var shown = model.ShownCatalog == connection.Id;

        menu.Items.Add(PageParts.MenuEntry(shown ? "Hide models" : "View models", () =>
        {
            if (shown)
            {
                HideCatalog();
            }
            else
            {
                Run(() => model.ShowCatalog(connection));
            }
        }, enabled));
        if (shown && model.CanEdit)
        {
            menu.Items.Add(PageParts.MenuEntry("Refresh models", () => Run(() => model.RefreshCatalog(connection)), enabled));
        }

        if (model.CanEdit)
        {
            menu.Items.Add(PageParts.MenuEntry("Test connection", () => Run(() => model.Test(connection)), enabled));
            menu.Items.Add(PageParts.MenuEntry("Replace key", () => OpenReplace(connection), enabled));
        }

        return menu.Items.Count == 0 ? null : menu;
    }

    private void HideCatalog()
    {
        model.HideCatalog();
        Refresh();
    }

    private UIElement CatalogRow(
        Drover.Contracts.Models.ModelConnectionSummary connection,
        Drover.Contracts.Models.ModelCatalogResponse entry)
    {
        var state = entry.State is "available" ? string.Empty : $" ({entry.State})";
        var words = ModelConnectionsViewModel.VisionWords(entry) is { } vision ? $" · {vision}" : string.Empty;
        var line = Styles.Text($"{entry.DisplayName} · {entry.Model}{state}{(model.CanMarkVision(connection) ? string.Empty : words)}");

        if (!model.CanMarkVision(connection))
        {
            return line;
        }

        var reads = new Switch
        {
            Label = ModelConnectionsViewModel.VisionLabel(entry),
            Value = entry.Capabilities.Vision,
            IsEnabled = !model.ChangePending,
        };
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
        catch (Exception exception)
        {
            logger.LogError(exception, "An action on the model connections page failed");
        }
        finally
        {
            Refresh();
        }
    }

    #endregion
}
