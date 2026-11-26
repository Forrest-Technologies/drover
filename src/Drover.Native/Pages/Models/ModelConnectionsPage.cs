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

    private readonly TextBlock providersLoading = PageParts.Waiting(NamedProviders.LoadingWords);

    private readonly StatusNote providersFailed = new() { Kind = NoteKind.Danger, Text = NamedProviders.FailedWords };

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

    private readonly Sheet settingsSheet = new();

    private readonly Dialog clearDialog;

    private readonly TextBlock settingsIntro = Styles.Text(string.Empty, "DvTextSecondary");

    private readonly TextBlock clearBody = Styles.Text(string.Empty);

    private readonly TextBlock effortsLabel = Styles.Text("Effort levels", "DvTextLabel");

    private readonly TextBlock priceHeading = Styles.Text("Price per million tokens", "DvTextLabel");

    private readonly Switch toolsSwitch = new() { Label = "Calls tools" };

    private readonly Switch visionSwitch = new() { Label = "Reads images" };

    private readonly Switch reasoningSwitch = new() { Label = "Thinks before answering" };

    private readonly Switch structuredSwitch = new() { Label = "Answers in a set shape" };

    private readonly Switch imageSwitch = new() { Label = "Makes images" };

    private readonly Dictionary<string, Switch> effortSwitches = ModelSettingsForm.EffortLevels.ToDictionary(level => level, level => new Switch { Label = level });

    private readonly StackPanel efforts = PageParts.Column();

    private readonly Field contextField;

    private readonly Field outputField;

    private readonly Field inputPriceField;

    private readonly Field outputPriceField;

    private readonly Field cachedPriceField;

    private readonly Field currencyField;

    private readonly StatusNote settingsProblem = new() { Kind = NoteKind.Danger };

    private readonly StatusNote settingsBusy = new() { Kind = NoteKind.Info };

    private readonly Button useProvidersValues;

    private readonly Button saveSettings;

    private readonly Button keepThem;

    private readonly Button removeSettings;

    private readonly Button retryProviders;

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
        baseUrlField = PageParts.Field("Base address", string.Empty, value => model.BaseUrl = value, "url",
            "Any server that speaks the chat completions API.");
        keyField = PageParts.Field("Key", string.Empty, value => model.ApiKey = value, "password", model.Named.KeyHint);
        newKeyField = PageParts.Field("New key", string.Empty, value => model.NewKey = value, "password",
            "Stored encrypted. It is not shown again.");
        provider.RegisterPropertyChangedCallback(Select.ValueProperty, (_, _) =>
        {
            if (provider.Value.Length > 0 && provider.Value != model.Named.Choice)
            {
                model.ChooseProvider(provider.Value);
                UpdateAddForm();
            }
        });
        retryProviders = PageParts.Action(NamedProviders.RetryWords, async () => { await model.RetryProviders(); UpdateAddForm(); }, UpdateAddForm);
        changeAddress = PageParts.Action(NamedProviders.ChangeAddressWords, () => { model.ChangeAddress(); UpdateAddForm(); return Task.CompletedTask; }, UpdateAddForm);
        nameField.Submitted += (_, _) => Run(model.Add);
        baseUrlField.Submitted += (_, _) => Run(model.Add);
        keyField.Submitted += (_, _) => Run(model.Add);
        newKeyField.Submitted += (_, _) => Run(ReplaceKey);

        cancelAdd = PageParts.Action("Cancel", CancelAdd, Refresh);
        addButton = PageParts.Action("Add and test", model.Add, Refresh, kind: ButtonKind.Primary);
        addSheet.Body = PageParts.SheetBody(provider, providersLoading, providersFailed, retryProviders, nameField, reach, baseUrlField, changeAddress, keyField, keyLink, addProblem, addBusy);
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

        contextField = PageParts.Field("Context size (tokens)", string.Empty, value => model.Settings.ContextSize = value, "number");
        outputField = PageParts.Field("Longest answer (tokens)", string.Empty, value => model.Settings.MaxOutputSize = value, "number");
        inputPriceField = PageParts.Field("Input", string.Empty, value => model.Settings.InputPrice = value, "number");
        outputPriceField = PageParts.Field("Output", string.Empty, value => model.Settings.OutputPrice = value, "number");
        cachedPriceField = PageParts.Field("Cached", string.Empty, value => model.Settings.CachedPrice = value, "number");
        currencyField = PageParts.Field("Currency", string.Empty, value => model.Settings.Currency = value);
        currencyField.MaxLength = FieldLimits.CurrencyCode;
        BindSwitch(toolsSwitch, on => model.Settings.ToolCalling = on);
        BindSwitch(visionSwitch, on => model.Settings.Vision = on);
        BindSwitch(reasoningSwitch, on => { model.Settings.Reasoning = on; UpdateSettingsForm(); });
        BindSwitch(structuredSwitch, on => model.Settings.StructuredOutput = on);
        BindSwitch(imageSwitch, on => model.Settings.ImageGeneration = on);

        foreach (var (level, toggle) in effortSwitches)
        {
            BindSwitch(toggle, on => _ = on ? model.Settings.Efforts.Add(level) : model.Settings.Efforts.Remove(level));
            efforts.Children.Add(toggle);
        }

        foreach (var price in (Field[])[inputPriceField, outputPriceField, cachedPriceField, currencyField])
        {
            price.MinWidth = Tokens.Number("DvSpace7") * 3;
        }

        var prices = PageParts.Row(inputPriceField, outputPriceField, cachedPriceField, currencyField);
        useProvidersValues = PageParts.Action("Use the provider's values", () => { model.AskUseProvidersValues(); return Task.CompletedTask; }, Refresh);
        saveSettings = PageParts.Action("Save", model.SaveSettings, Refresh, kind: ButtonKind.Primary);
        settingsSheet.Body = PageParts.SheetBody(
            settingsIntro, contextField, outputField, toolsSwitch, visionSwitch, reasoningSwitch, effortsLabel, efforts,
            structuredSwitch, imageSwitch, priceHeading, prices, settingsProblem, settingsBusy);
        settingsSheet.Actions = PageParts.Row(useProvidersValues, saveSettings);
        settingsSheet.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (!settingsSheet.Open && model.EditingSettings)
            {
                model.CancelSettings();
                Refresh();
            }
        });

        keepThem = PageParts.Action(ModelSettingsForm.KeepThem, () => { model.KeepSettings(); return Task.CompletedTask; }, Refresh);
        removeSettings = PageParts.Action("Remove", model.UseProvidersValues, Refresh);
        removeSettings.Kind = ButtonKind.Danger;
        clearDialog = new Dialog { Title = ModelSettingsForm.UseProviderTitle, InitialFocus = keepThem };
        clearDialog.Body = PageParts.SheetBody(clearBody);
        clearDialog.Actions = PageParts.Row(keepThem, removeSettings);
        clearDialog.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (!clearDialog.Open && model.ConfirmingClear)
            {
                model.KeepSettings();
                Refresh();
            }
        });

        layout.Body.Add(headerContent);
        layout.Body.Add(addSheet);
        layout.Body.Add(replaceDialog);
        layout.Body.Add(settingsSheet);
        layout.Body.Add(clearDialog);
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
        UpdateSettingsForm();
        addSheet.Open = model.Adding;
        replaceDialog.Open = model.Replacing is not null;
        settingsSheet.Open = model.EditingSettings;
        clearDialog.Open = model.ConfirmingClear;
    }

    private static void BindSwitch(Switch toggle, Action<bool> changed) =>
        toggle.RegisterPropertyChangedCallback(Switch.ValueProperty, (_, _) => changed(toggle.Value));

    private void OpenSettings(Drover.Contracts.Models.ModelConnectionSummary connection, Drover.Contracts.Models.ModelCatalogResponse entry)
    {
        model.OpenSettings(connection, entry);
        settingsSheet.Title = entry.DisplayName;
        UpdateSettingsForm();
        settingsSheet.Open = true;
        settingsSheet.DispatcherQueue.TryEnqueue(() => contextField.TakeFocus());
    }

    private void UpdateSettingsForm()
    {
        var form = model.Settings;
        var busy = model.SettingsBusy;
        settingsIntro.Text = form.Intro;
        ShowNumber(contextField, form.ContextSize, nameof(ModelSettingsForm.ContextSize), "contextSize", busy);
        ShowNumber(outputField, form.MaxOutputSize, nameof(ModelSettingsForm.MaxOutputSize), "maxOutputSize", busy);
        ShowSwitch(toolsSwitch, form.ToolCalling, "toolCalling", busy);
        ShowSwitch(visionSwitch, form.Vision, "vision", busy);
        ShowSwitch(reasoningSwitch, form.Reasoning, "reasoning", busy);
        ShowSwitch(structuredSwitch, form.StructuredOutput, "structuredOutput", busy);
        ShowSwitch(imageSwitch, form.ImageGeneration, "imageGeneration", busy);
        var shown = form.ShowsEfforts ? Visibility.Visible : Visibility.Collapsed;
        effortsLabel.Visibility = shown;
        efforts.Visibility = shown;

        foreach (var (level, toggle) in effortSwitches)
        {
            ShowSwitch(toggle, form.Efforts.Contains(level), "reasoningEfforts", busy);
        }

        ShowNumber(inputPriceField, form.InputPrice, nameof(ModelSettingsForm.InputPrice), "inputPerMillion", busy);
        ShowNumber(outputPriceField, form.OutputPrice, nameof(ModelSettingsForm.OutputPrice), "outputPerMillion", busy);
        ShowNumber(cachedPriceField, form.CachedPrice, nameof(ModelSettingsForm.CachedPrice), "cachedInputPerMillion", busy);
        currencyField.Disabled = busy;
        currencyField.Error = form.Problems.GetValueOrDefault(nameof(ModelSettingsForm.Currency));

        if (currencyField.Value != form.Currency)
        {
            currencyField.Value = form.Currency;
        }

        settingsProblem.Text = model.SettingsProblem;
        settingsBusy.Text = busy ? "Saving what you set." : string.Empty;
        settingsBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        useProvidersValues.Visibility = form.OffersUseProvidersValues ? Visibility.Visible : Visibility.Collapsed;
        useProvidersValues.IsEnabled = !busy;
        saveSettings.IsEnabled = !busy;
        clearBody.Text = form.UseProvidersValuesBody;
        keepThem.IsEnabled = !busy;
        removeSettings.IsEnabled = !busy;
    }

    private void ShowNumber(Field field, string value, string problem, string member, bool busy)
    {
        field.Disabled = busy;
        field.Error = model.Settings.Problems.GetValueOrDefault(problem);
        field.Hint = model.Settings.Caption(member);

        if (field.Value != value)
        {
            field.Value = value;
        }
    }

    private void ShowSwitch(Switch toggle, bool on, string member, bool busy)
    {
        toggle.IsEnabled = !busy;
        toggle.About = model.Settings.Caption(member);

        if (toggle.Value != on)
        {
            toggle.Value = on;
        }
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

        provider.Disabled = busy;
        providersLoading.Visibility = providers.ShowsLoading ? Visibility.Visible : Visibility.Collapsed;
        providersFailed.Visibility = providers.ShowsFailure ? Visibility.Visible : Visibility.Collapsed;
        retryProviders.Visibility = providers.ShowsFailure ? Visibility.Visible : Visibility.Collapsed;
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
        addButton.IsEnabled = !busy;
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
        var line = Styles.Text($"{entry.DisplayName} · {entry.Model}{state}{words}");
        line.TextWrapping = TextWrapping.Wrap;

        if (!model.CanEdit)
        {
            return line;
        }

        var menu = new Menu
        {
            Label = "More",
            AccessibleLabel = $"More for {entry.DisplayName}",
            TriggerIcon = IconName.More,
            ShowTriggerLabel = false,
        };
        menu.Items.Add(PageParts.MenuEntry("Edit what this model can do", () => OpenSettings(connection, entry), !model.ChangePending));

        var row = new Grid { ColumnSpacing = Tokens.Number("DvSpace3") };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        line.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(menu, 1);
        row.Children.Add(line);
        row.Children.Add(menu);
        return row;
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
