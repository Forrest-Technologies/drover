namespace Drover.Native.Pages;

/// <summary>Add and inspect tool servers, sign in, review catalog changes, and remove connections.</summary>
public sealed class ToolConnectionsPage : Screen
{
    #region Private Fields

    private static readonly ILogger logger = Log.For<ToolConnectionsPage>();

    private readonly WorkLayout layout = new();

    private readonly StackPanel headerContent = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly ToolConnectionsViewModel model = Make<ToolConnectionsViewModel>();

    private Drover.Ui.Tools.ConnectionDraft draft = new();

    private readonly Sheet addSheet = new() { Title = "Add a tool server" };

    private readonly Dialog replaceDialog = new();

    private readonly Dialog removeDialog = new();

    private readonly SegmentedControl kind = new()
    {
        Label = "How Drover proves who is calling",
        Options = [new("none", "Nothing"), new("header", "A header"), new("oauth", "A sign-in")],
    };

    private readonly StackPanel kindPart;

    private readonly Field nameField;

    private readonly Field addressField;

    private readonly StackPanel headerPart;

    private readonly StackPanel oauthPart;

    private readonly Select signInScope;

    private readonly TextBlock scopeNote;

    private readonly Switch ownClient;

    private readonly StackPanel clientPart;

    private readonly TextBlock noClientNote;

    private readonly Field clientIdField;

    private readonly Field clientSecretField;

    private readonly Field issuerField;

    private readonly Switch trust;

    private readonly Select protocol;

    private readonly Field headerNameField;

    private readonly Field headerValueField;

    private readonly Field replaceNameField;

    private readonly Field replaceValueField;

    private readonly StatusNote addProblem = new() { Kind = NoteKind.Danger };

    private readonly StatusNote addBusy = new() { Kind = NoteKind.Info };

    private readonly StatusNote replaceProblem = new() { Kind = NoteKind.Danger };

    private readonly StatusNote replaceBusy = new() { Kind = NoteKind.Info };

    private readonly Button cancelAdd;

    private readonly Button addButton;

    private readonly Button cancelReplace;

    private readonly Button saveHeader;

    private readonly Button keepServer;

    private readonly Button removeServer;

    #endregion

    #region Constructors

    public ToolConnectionsPage()
    {
        nameField = Field("Name", value => draft.Name = value, hint: "What you call it, such as Calendar.");
        nameField.MaxLength = FieldLimits.ToolServerName;
        AutomationProperties.SetAutomationId(nameField, "ToolServerName");
        addressField = Field("Address", value => draft.Url = value, "url", "The address of the server's MCP endpoint.");
        addressField.Placeholder = "https://";
        headerNameField = Field("Header name", value => draft.HeaderName = value);
        headerNameField.MaxLength = FieldLimits.HeaderName;
        headerValueField = Field("Header value", value => draft.HeaderValue = value, "password", "Stored encrypted. It is not shown again.");
        headerPart = PageParts.Column(headerNameField, headerValueField);

        signInScope = Choice("Whose sign-in", value => draft.SignInScope = value,
            [new("per_user", "Each person signs in as themselves"), new("space", "One sign-in for the whole space")]);
        scopeNote = Styles.Text(string.Empty, "DvTextSecondary");
        ownClient = Toggle("Use a client I registered with the server", value => draft.OwnClient = value);
        clientIdField = Field("Client id", value => draft.ClientId = value);
        clientSecretField = Field("Client secret", value => draft.ClientSecret = value, "password",
            "Leave empty for a public client. It is not shown again.");
        issuerField = Field("Sign-in service address", value => draft.Issuer = value, "url",
            "The service that gave out the client id. The id is sent nowhere else.");
        clientPart = PageParts.Column(clientIdField, clientSecretField, issuerField);
        noClientNote = Styles.Text("Without one, Drover introduces itself to the server when someone first signs in.", "DvTextSecondary");
        oauthPart = PageParts.Column(signInScope, scopeNote, ownClient, clientPart, noClientNote);

        var kindName = Styles.Text(kind.Label, "DvTextLabel");
        AutomationProperties.SetAccessibilityView(kindName, AccessibilityView.Raw);
        kindPart = new StackPanel { Spacing = Tokens.Number("DvSpace2"), Children = { kindName, kind } };
        trust = Toggle("Trust what this server's tools return", value => draft.Trusted = value);
        protocol = Choice("Protocol", value => draft.ProtocolMode = value,
            [new("automatic", "Chosen by Drover"), new("stateless_only", "Without sessions only"), new("session_only", "With sessions only")]);
        var more = new Disclosure
        {
            Title = "More choices",
            Body = PageParts.Column(trust,
                Styles.Text(
                    "Leave this off unless you run the server. A result from a server that is not trusted is recorded on the run as untrusted text. It does not change what any bot may do.",
                    "DvTextSecondary"),
                protocol),
        };

        kind.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) =>
        {
            draft.AuthKind = kind.Value ?? "none";
            UpdateAddForm();
        });
        foreach (var field in new[] { nameField, addressField, headerNameField, headerValueField, clientIdField, clientSecretField, issuerField })
        {
            field.Submitted += (_, _) => Run(AddServer);
        }

        cancelAdd = PageParts.Action("Cancel", CancelAdd, Refresh);
        addButton = PageParts.Action("Add server", AddServer, Refresh, kind: ButtonKind.Primary);
        addSheet.Body = PageParts.SheetBody(nameField, addressField, kindPart, headerPart, oauthPart, more, addProblem, addBusy);
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

        replaceNameField = PageParts.Field("Header name", string.Empty, value => model.HeaderName = value);
        replaceNameField.MaxLength = FieldLimits.HeaderName;
        replaceValueField = PageParts.Field("Header value", string.Empty, value => model.HeaderValue = value, "password",
            "Stored encrypted. It is not shown again.");
        replaceNameField.Submitted += (_, _) => Run(SaveHeader);
        replaceValueField.Submitted += (_, _) => Run(SaveHeader);
        cancelReplace = PageParts.Action("Cancel", CancelReplace, Refresh);
        saveHeader = PageParts.Action("Save header", SaveHeader, Refresh, kind: ButtonKind.Primary);
        replaceDialog.Body = PageParts.SheetBody(replaceNameField, replaceValueField, replaceProblem, replaceBusy);
        replaceDialog.Actions = PageParts.Row(cancelReplace, saveHeader);
        replaceNameField.Loaded += (_, _) => { if (replaceDialog.Open) { replaceNameField.TakeFocus(); } };
        replaceDialog.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (replaceDialog.Open)
            {
                replaceDialog.DispatcherQueue.TryEnqueue(() => replaceNameField.TakeFocus());
            }
            else
            {
                CloseReplace();
            }
        });

        keepServer = Action("Keep server", CancelRemove);
        removeServer = Action("Remove server", model.Remove, ButtonKind.Danger);
        removeDialog.Body = Styles.Text(
            "Bots lose its tools at once, and its stored credential and sign-ins are deleted. Runs that already used it keep their history.");
        removeDialog.Actions = PageParts.Row(keepServer, removeServer);
        removeDialog.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (!removeDialog.Open)
            {
                model.Removing = null;
            }
        });

        layout.Body.Add(headerContent);
        layout.Body.Add(addSheet);
        layout.Body.Add(replaceDialog);
        layout.Body.Add(removeDialog);
        Unloaded += (_, _) => CloseAll();
        Content = layout;
        Refresh();
    }

    #endregion

    #region Properties

    // Where the provider's sign-in page comes back to: a device app's scheme, the desktop app's loopback listener, or
    // the browser build's own path.
    private static string? ReturnScheme => ((App)Application.Current).Services.GetService<AppSignInReturn>()?.ReturnTo;

    public override string Title => "Tool connections · Drover";

    #endregion

    #region Private Methods

    protected override async Task Opened(RouteMatch<Type> route)
    {
        await model.Open(route.QueryValue("sign-in"), Guid.TryParse(route.QueryValue("connection"), out var id) ? id : null, route.QueryValue("ticket"));
        Refresh();
    }

    private void Refresh()
    {
        headerContent.Children.Clear();

        var header = new ScreenHeader { Title = "Tool connections" };
        if (model.CanEdit && model.Connections is { Count: > 0 })
        {
            var add = new IconButton
            {
                Icon = IconName.Plus,
                Label = "Add a tool server",
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetAutomationId(add, "AddToolServer");
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
                Title = "Drover could not load the tool connections",
                Cause = problem,
                ActionLabel = "Try again",
            };
            failed.Action += async (_, _) => { await model.Load(); Refresh(); };
            headerContent.Children.Add(failed);
        }
        else if (model.Connections is null)
        {
            headerContent.Children.Add(PageParts.Waiting("Loading the tool connections."));
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
        replaceDialog.Open = model.ReplacingHeader is not null;
        removeDialog.Open = model.Removing is not null;
        removeServer.IsEnabled = model.Removing is not null && !model.ChangePending;
    }

    private UIElement Empty()
    {
        if (!model.CanEdit)
        {
            return Styles.Text("This space has no tool servers yet. An owner or admin of the space can add one.", "DvTextSecondary");
        }

        var empty = new EmptyState
        {
            Title = "No tool server yet",
            Cause = "A tool server gives bots tools beyond the built-in ones. Add one by its address, then give its tools to a bot.",
            ActionLabel = "Add a tool server",
        };
        empty.Action += (_, _) => OpenAddSheet();
        return empty;
    }

    private void OpenAddSheet()
    {
        draft = new();
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
        if (model.Adding || draft.HeaderValue.Length > 0 || draft.ClientSecret.Length > 0)
        {
            model.Adding = false;
        }

        draft = new();
        UpdateAddForm();
    }

    private Task CancelReplace()
    {
        CloseReplace();
        return Task.CompletedTask;
    }

    private void CloseReplace()
    {
        if (model.ReplacingHeader is not null)
        {
            model.CancelReplacingHeader();
        }

        UpdateReplaceForm();
    }

    private Task CancelRemove()
    {
        model.Removing = null;
        return Task.CompletedTask;
    }

    private void OpenRemove(Drover.Contracts.Tools.ToolConnectionSummary connection)
    {
        model.Removing = connection;
        removeDialog.Title = $"Remove {connection.Name}";
        removeDialog.Open = true;
        removeServer.IsEnabled = !model.ChangePending;
    }

    private void CloseAll()
    {
        model.Adding = false;
        model.CancelReplacingHeader();
        model.Removing = null;
        draft = new();
        UpdateAddForm();
        UpdateReplaceForm();
    }

    private Task AddServer() => model.Add(draft);

    private Task SaveHeader()
    {
        var connection = Replacing();
        return connection is null ? Task.CompletedTask : model.ReplaceHeader(connection);
    }

    private void UpdateAddForm()
    {
        var busy = model.AddingBusy;
        if (kind.Value != draft.AuthKind)
        {
            kind.Value = draft.AuthKind;
        }

        kind.Disabled = busy;
        nameField.Value = draft.Name;
        nameField.Disabled = busy;
        addressField.Value = draft.Url;
        addressField.Disabled = busy;
        headerNameField.Value = draft.HeaderName;
        headerNameField.Disabled = busy;
        headerValueField.Value = draft.HeaderValue;
        headerValueField.Disabled = busy;
        headerPart.Visibility = draft.AuthKind is "header" ? Visibility.Visible : Visibility.Collapsed;
        oauthPart.Visibility = draft.AuthKind is "oauth" ? Visibility.Visible : Visibility.Collapsed;
        signInScope.Value = draft.SignInScope;
        signInScope.Disabled = busy;
        scopeNote.Text = draft.SignInScope is "space"
            ? "Every bot run in the space acts as the one account an owner or admin signs in with."
            : "A bot run acts as the person it runs for, and can use this server once that person has signed in.";
        ownClient.Value = draft.OwnClient;
        ownClient.IsEnabled = !busy;
        clientPart.Visibility = draft.OwnClient ? Visibility.Visible : Visibility.Collapsed;
        noClientNote.Visibility = draft.OwnClient ? Visibility.Collapsed : Visibility.Visible;
        clientIdField.Value = draft.ClientId;
        clientIdField.Disabled = busy;
        clientSecretField.Value = draft.ClientSecret;
        clientSecretField.Disabled = busy;
        issuerField.Value = draft.Issuer;
        issuerField.Disabled = busy;
        trust.Value = draft.Trusted;
        trust.IsEnabled = !busy;
        protocol.Value = draft.ProtocolMode;
        protocol.Disabled = busy;
        addProblem.Text = model.AddProblem;
        addBusy.Text = busy ? "Adding the server." : string.Empty;
        addBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        cancelAdd.IsEnabled = !busy;
        addButton.IsEnabled = !busy;
    }

    private void UpdateReplaceForm()
    {
        var busy = model.ChangePending;
        if (model.ReplacingHeader is { } connection)
        {
            replaceDialog.Title = $"Replace the header for {connection.Name}";
        }

        replaceNameField.Value = model.ReplacingHeader is null ? string.Empty : model.HeaderName;
        replaceNameField.Disabled = busy;
        replaceValueField.Value = model.ReplacingHeader is null ? string.Empty : model.HeaderValue;
        replaceValueField.Disabled = busy;
        replaceProblem.Text = model.HeaderProblem;
        replaceBusy.Text = busy ? "Saving the header." : string.Empty;
        replaceBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        cancelReplace.IsEnabled = !busy;
        saveHeader.IsEnabled = !busy && model.ReplacingHeader is not null;
    }

    private Drover.Contracts.Tools.ToolConnectionSummary? Replacing() =>
        model.ReplacingHeader is { } id
            ? model.Connections?.FirstOrDefault(connection => connection.Id == id.Id)
            : null;

    private ListCard Connection(Drover.Contracts.Tools.ToolConnectionSummary connection)
    {
        var status = Drover.Ui.Tools.ConnectionWords.Status(connection);
        var tools = model.Tools.GetValueOrDefault(connection.Id);
        var card = new ListCard
        {
            Title = connection.Name,
            Facts = connection.Url,
            Status = new Badge { Text = status.Words, Kind = status.Kind },
            Trailing = CardMenu(connection),
        };

        var beneath = PageParts.Column();
        var shownError = connection.Status is "failed" ? connection.LastError : null;
        beneath.Children.Add(new StatusNote { Text = shownError, Kind = NoteKind.Danger });
        beneath.Children.Add(new StatusNote
        {
            Text = model.Problems.GetValueOrDefault(connection.Id) is { } problem && problem != shownError ? problem : null,
            Kind = NoteKind.Danger,
        });

        var maySignIn = connection.SignInScope is "per_user" || model.CanEdit;
        if (Drover.Ui.Tools.ConnectionWords.SignInNeed(connection) is { } need)
        {
            beneath.Children.Add(Styles.Text(need));
            beneath.Children.Add(maySignIn
                ? Action(connection.SignIn is "needs_reconnect" ? "Sign in again" : "Sign in",
                    () => model.SignIn(connection, PlatformShell.OpenOutside, ReturnScheme), ButtonKind.Quiet)
                : Styles.Text("An owner or admin of the space signs in for everyone.", "DvTextSecondary"));
        }

        beneath.Children.Add(Styles.Text(Drover.Ui.Tools.ConnectionWords.Facts(connection, tools), "DvTextMono"));

        if (tools is { Count: > 0 })
        {
            var entries = PageParts.Column();
            foreach (var tool in tools)
            {
                var entry = PageParts.Column(
                    Styles.Text(string.IsNullOrWhiteSpace(tool.Title) ? tool.Name : tool.Title, "DvTextH3"),
                    Styles.Text(tool.Name, "DvTextMono"),
                    Styles.Text(tool.Description, "DvTextSecondary"));
                if (tool.State is "missing")
                {
                    entry.Children.Add(Styles.Text("The server no longer lists this tool, so no bot can use it.", "DvTextSecondary"));
                }

                if (tool.NeedsReview)
                {
                    ToolReview(entry, tool);
                    if (model.CanEdit)
                    {
                        entry.Children.Add(Action(
                            $"Accept the change to {(string.IsNullOrWhiteSpace(tool.Title) ? tool.Name : tool.Title)}",
                            () => model.Accept(tool)));
                    }
                }

                entries.Children.Add(entry);
            }

            beneath.Children.Add(new Disclosure { Title = "Tools", Open = tools.Any(tool => tool.NeedsReview), Body = entries });
        }
        else if (tools is { Count: 0 } && connection.Status is "ready")
        {
            beneath.Children.Add(Styles.Text("The server answers and lists no tools.", "DvTextSecondary"));
        }

        while (beneath.Children.Count > 0 && beneath.Children[0] is StatusNote { Text: null })
        {
            beneath.Children.RemoveAt(0);
        }

        card.Beneath = beneath.Children.Count > 0 ? beneath : null;
        return card;
    }

    private void ToolReview(StackPanel entry, Drover.Contracts.Tools.ConnectionToolSummary tool)
    {
        entry.Children.Add(new Badge { Text = "Changed, needs review", Kind = BadgeKind.Warn });
        entry.Children.Add(Styles.Text(
            "The server changed what this tool says it does. Bots that have it ask before each use until the change is accepted.",
            "DvTextSecondary"));
        if (tool.Reviewed is { } reviewed)
        {
            entry.Children.Add(Styles.Text("Reviewed", "DvTextLabel"));
            entry.Children.Add(Styles.Text($"{(string.IsNullOrWhiteSpace(reviewed.Title) ? tool.Name : reviewed.Title)}\n{reviewed.Description}"));
            entry.Children.Add(Styles.Text("Now", "DvTextLabel"));
            entry.Children.Add(Styles.Text($"{(string.IsNullOrWhiteSpace(tool.Title) ? tool.Name : tool.Title)}\n{tool.Description}"));
            if (reviewed.Title == tool.Title && reviewed.Description == tool.Description)
            {
                entry.Children.Add(Styles.Text("The words are the same. What the tool accepts as input changed.", "DvTextSecondary"));
            }
        }
    }

    private Menu? CardMenu(Drover.Contracts.Tools.ToolConnectionSummary connection)
    {
        var menu = new Menu
        {
            Label = "More",
            AccessibleLabel = $"More for {connection.Name}",
            TriggerIcon = IconName.More,
            ShowTriggerLabel = false,
        };
        var enabled = !model.ChangePending;

        if (model.CanEdit)
        {
            menu.Items.Add(PageParts.MenuEntry("Check and list tools", () => Run(() => model.Refresh(connection)), enabled));
            if (connection.AuthKind is "header")
            {
                menu.Items.Add(PageParts.MenuEntry("Replace header", () => OpenReplace(connection), enabled));
            }
        }

        var maySignIn = connection.SignInScope is "per_user" || model.CanEdit;
        if (connection.SignIn is "signed_in" && maySignIn)
        {
            menu.Items.Add(PageParts.MenuEntry($"Sign out of {connection.Name}", () => Run(() => model.SignOut(connection)), enabled));
        }

        if (model.CanEdit)
        {
            menu.Items.Add(PageParts.MenuEntry($"Remove {connection.Name}", () => OpenRemove(connection), enabled));
        }

        return menu.Items.Count == 0 ? null : menu;
    }

    private void OpenReplace(Drover.Contracts.Tools.ToolConnectionSummary connection)
    {
        model.StartReplacingHeader(connection);
        replaceDialog.Title = $"Replace the header for {connection.Name}";
        UpdateReplaceForm();
        replaceDialog.Open = true;
        replaceDialog.DispatcherQueue.TryEnqueue(() => replaceNameField.TakeFocus());
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
            logger.LogError(exception, "An action on the tool connections page failed");
        }
        finally
        {
            Refresh();
        }
    }

    private Field Field(string label, Action<string> change, string type = "text", string? hint = null) =>
        PageParts.Field(label, string.Empty, change, type, hint, model.ChangePending);

    private Select Choice(string label, Action<string> change, IReadOnlyList<SelectOption> options)
    {
        var select = new Select { Label = label, Options = options, Value = string.Empty, Disabled = model.ChangePending };
        select.RegisterPropertyChangedCallback(Select.ValueProperty, (_, _) => { change(select.Value); UpdateAddForm(); });
        return select;
    }

    private Switch Toggle(string label, Action<bool> change)
    {
        var toggle = new Switch { Label = label, Value = false, IsEnabled = !model.ChangePending };
        toggle.RegisterPropertyChangedCallback(Switch.ValueProperty, (_, _) => { change(toggle.Value); UpdateAddForm(); });
        return toggle;
    }

    private Button Action(string label, Action work, ButtonKind kind = ButtonKind.Quiet) =>
        Action(label, () => { work(); return Task.CompletedTask; }, kind);

    private Button Action(string label, Func<Task> work, ButtonKind kind = ButtonKind.Quiet) =>
        PageParts.Action(label, work, Refresh, !model.ChangePending, kind);

    #endregion
}
