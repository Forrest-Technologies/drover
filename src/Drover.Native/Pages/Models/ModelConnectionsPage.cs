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

    private readonly SegmentedControl provider = new() { Label = "Provider" };

    private readonly Field nameField;

    private readonly Field baseUrlField;

    private readonly Field keyField;

    private readonly TextBlock signInLead = Styles.Text(null, "DvTextSecondary");

    private readonly TextBlock userCode = Styles.Text(null, "DvTextMono");

    private readonly CopyButton copyUserCode = new() { Label = "Copy the sign-in code" };

    private readonly Link verification = new() { Text = "Open the verification page" };

    private readonly TextBlock waiting = Styles.Text("Waiting for you to approve in the browser.", "DvTextSecondary");

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
        provider.Options = model.Providers;
        copyUserCode.Text = () => model.UserCode ?? string.Empty;
        AutomationProperties.SetName(userCode, "Sign-in code");
        AutomationProperties.SetLiveSetting(waiting, AutomationLiveSetting.Polite);
        userCode.IsTextSelectionEnabled = true;
        provider.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) =>
        {
            model.ChooseProvider(provider.Value ?? model.Kind);
            UpdateAddForm();
        });
        nameField.Submitted += (_, _) => Run(model.Add);
        baseUrlField.Submitted += (_, _) => Run(model.Add);
        keyField.Submitted += (_, _) => Run(model.Add);
        newKeyField.Submitted += (_, _) => Run(ReplaceKey);

        cancelAdd = PageParts.Action("Cancel", CancelAdd, Refresh);
        addButton = PageParts.Action("Add and test", model.Add, Refresh, kind: ButtonKind.Primary);
        addSheet.Body = PageParts.SheetBody(
            provider,
            nameField,
            baseUrlField,
            keyField,
            signInLead,
            PageParts.Row(userCode, copyUserCode),
            verification,
            waiting,
            addProblem,
            addBusy);
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
        provider.Options = model.Providers;
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
        if (provider.Value != model.Kind)
        {
            provider.Value = model.Kind;
        }

        if (provider.Options != model.Providers)
        {
            provider.Options = model.Providers;
        }

        provider.Disabled = busy || model.WaitingForApproval;
        nameField.Value = model.Name;
        nameField.Disabled = busy || model.WaitingForApproval;
        nameField.Visibility = model.IsSignIn ? Visibility.Collapsed : Visibility.Visible;
        baseUrlField.Value = model.BaseUrl;
        baseUrlField.Disabled = busy;
        keyField.Value = model.ApiKey;
        keyField.Disabled = busy;
        keyField.Hint = KeyHint();
        keyField.Visibility = model.IsSignIn ? Visibility.Collapsed : Visibility.Visible;
        baseUrlField.Visibility = model.IsSignIn
            ? Visibility.Collapsed
            : model.Kind is "openai_compatible" ? Visibility.Visible : Visibility.Collapsed;
        signInLead.Text = model.WaitingForApproval
            ? "Enter this code on the verification page, then come back here."
            : $"Continue with {model.SignInName} to sign in with your own plan. Usage bills that plan.";
        signInLead.Visibility = model.IsSignIn ? Visibility.Visible : Visibility.Collapsed;
        userCode.Text = model.UserCode ?? string.Empty;
        userCode.Visibility = string.IsNullOrEmpty(model.UserCode) ? Visibility.Collapsed : Visibility.Visible;
        copyUserCode.Visibility = string.IsNullOrEmpty(model.UserCode) ? Visibility.Collapsed : Visibility.Visible;
        verification.Address = model.VerificationAddress;
        verification.Visibility = string.IsNullOrEmpty(model.VerificationAddress) ? Visibility.Collapsed : Visibility.Visible;
        waiting.Visibility = model.WaitingForApproval ? Visibility.Visible : Visibility.Collapsed;
        addProblem.Text = model.AddProblem;
        addBusy.Text = busy ? (model.IsSignIn ? "Starting sign-in." : "Adding the connection.") : string.Empty;
        addBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        cancelAdd.IsEnabled = !busy;
        addButton.IsEnabled = !busy && !model.WaitingForApproval;
        addButton.Content = new TextBlock
        {
            Text = model.IsSignIn ? $"Continue with {model.SignInName}" : "Add and test",
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(addButton, model.IsSignIn ? $"Continue with {model.SignInName}" : "Add and test");
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

    private string KeyHint() => model.Kind is "openai_compatible"
        ? "Leave this empty if the server needs no key."
        : "Made in your Venice account, under API keys.";

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
            Facts = model.Facts(connection),
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

            if (connection.SignIn is not null)
            {
                if (model.NeedsSignIn(connection) || model.IsSignedIn(connection))
                {
                    menu.Items.Add(PageParts.MenuEntry("Sign in", () =>
                    {
                        OpenAddSheet();
                        Run(() => model.SignIn(connection));
                    }, enabled));
                }

                if (model.IsSignedIn(connection))
                {
                    menu.Items.Add(PageParts.MenuEntry("Sign out", () => Run(() => model.SignOut(connection)), enabled));
                }
            }
            else
            {
                menu.Items.Add(PageParts.MenuEntry("Replace key", () => OpenReplace(connection), enabled));
            }
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
