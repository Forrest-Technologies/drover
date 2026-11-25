namespace Drover.Native.Pages;

/// <summary>Add and inspect tool servers, sign in, review catalog changes, and remove connections.</summary>
public sealed class ToolConnectionsPage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly ToolConnectionsViewModel model = Make<ToolConnectionsViewModel>();

    private Drover.Ui.Tools.ConnectionDraft draft = new();

    private readonly Sheet addSheet = new() { Title = "Add a tool server" };

    private readonly Dialog replaceDialog = new();

    private readonly SegmentedControl kind = new()
    {
        Label = "How Drover proves who is calling",
        Options = [new("none", "Nothing"), new("header", "A header"), new("oauth", "A sign-in")],
    };

    private readonly TextBlock kindName;

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
        headerValueField = Field("Header value", value => draft.HeaderValue = value, "password", "Stored encrypted. It is not shown again.");
        headerPart = Forms.Column(headerNameField, headerValueField);

        signInScope = Choice("Whose sign-in", value => draft.SignInScope = value,
            [new("per_user", "Each person signs in as themselves"), new("space", "One sign-in for the whole space")]);
        scopeNote = Styles.Text(string.Empty, "DvTextSecondary");
        ownClient = Toggle("Use a client I registered with the server", value => draft.OwnClient = value);
        clientIdField = Field("Client id", value => draft.ClientId = value);
        clientSecretField = Field("Client secret", value => draft.ClientSecret = value, "password", "Leave empty for a public client. It is not shown again.");
        issuerField = Field("Sign-in service address", value => draft.Issuer = value, "url", "The service that gave out the client id. The id is sent nowhere else.");
        clientPart = Forms.Column(clientIdField, clientSecretField, issuerField);
        noClientNote = Styles.Text("Without one, Drover introduces itself to the server when someone first signs in.", "DvTextSecondary");
        oauthPart = Forms.Column(signInScope, scopeNote, ownClient, clientPart, noClientNote);

        kindName = Styles.Text(kind.Label, "DvTextLabel");
        AutomationProperties.SetAccessibilityView(kindName, AccessibilityView.Raw);
        trust = Toggle("Trust what this server's tools return", value => draft.Trusted = value);
        protocol = Choice("Protocol", value => draft.ProtocolMode = value,
            [new("automatic", "Chosen by Drover"), new("stateless_only", "Without sessions only"), new("session_only", "With sessions only")]);
        var more = new Disclosure
        {
            Title = "More choices",
            Body = Forms.Column(trust,
                Styles.Text("Leave this off unless you run the server. A result from a server that is not trusted is recorded on the run as untrusted text. It does not change what any bot may do.", "DvTextSecondary"),
                protocol),
        };

        kind.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) =>
        {
            draft.AuthKind = kind.Value ?? "none";
            UpdateAddForm();
        });

        cancelAdd = PageParts.Action("Cancel", () => { model.Adding = false; draft = new(); return Task.CompletedTask; }, Refresh);
        addButton = PageParts.Action("Add server", async () =>
        {
            await model.Add(draft);
            if (!model.Adding)
            {
                draft = new();
            }
        }, Refresh, kind: ButtonKind.Primary);
        addSheet.Body = Forms.SheetBody(nameField, addressField, kindName, kind, headerPart, oauthPart, more, addProblem, addBusy,
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
                draft = new();
            }
        });

        replaceNameField = PageParts.Field("Header name", string.Empty, value => model.HeaderName = value);
        replaceValueField = PageParts.Field("Header value", string.Empty, value => model.HeaderValue = value, "password",
            "Stored encrypted. It is not shown again.");
        cancelReplace = PageParts.Action("Cancel", () => { model.CancelReplacingHeader(); return Task.CompletedTask; }, Refresh);
        saveHeader = PageParts.Action("Save header", () => model.ReplaceHeader(Replacing()), Refresh, kind: ButtonKind.Primary);
        replaceDialog.Body = Forms.SheetBody(replaceNameField, replaceValueField, replaceProblem, replaceBusy,
            PageParts.Row(cancelReplace, saveHeader));
        replaceNameField.Loaded += (_, _) => { if (replaceDialog.Open) { replaceNameField.TakeFocus(); } };
        replaceDialog.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (replaceDialog.Open)
            {
                replaceDialog.DispatcherQueue.TryEnqueue(() => replaceNameField.TakeFocus());
            }
            else if (model.ReplacingHeader is not null)
            {
                model.CancelReplacingHeader();
            }
        });

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
        var body = layout.Body;
        body.Clear();

        var header = new ScreenHeader { Title = "Tool connections" };
        if (model.CanEdit && model.Connections is { Count: > 0 } && !model.Adding)
        {
            var add = new IconButton
            {
                Icon = IconName.Plus,
                Label = "Add a tool server",
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetAutomationId(add, "AddToolServer");
            add.Click += (_, _) => { draft = new(); model.StartAdding(); Refresh(); };
            header.Actions.Add(add);
        }

        body.Add(header);
        body.Add(new StatusNote { Text = model.Notice, Kind = model.NoticeIsProblem ? NoteKind.Danger : NoteKind.Ok });

        if (model.LoadProblem is { } problem)
        {
            var failed = new EmptyState { Title = "Drover could not load the tool connections", Cause = problem, ActionLabel = "Try again" };
            failed.Action += async (_, _) => { await model.Load(); Refresh(); };
            body.Add(failed);
        }
        else if (model.Connections is null)
        {
            body.Add(PageParts.Waiting("Loading the tool connections."));
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

        if (model.Removing is { } removing)
        {
            var dialog = new Dialog
            {
                Title = $"Remove {removing.Name}",
                Body = Styles.Text("Bots lose its tools at once, and its stored credential and sign-ins are deleted. Runs that already used it keep their history."),
                Actions = PageParts.Row(Action("Keep server", () => model.Removing = null), Action("Remove server", model.Remove, ButtonKind.Danger)),
                Open = true,
            };
            dialog.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) => { if (!dialog.Open) { model.Removing = null; } });
            body.Add(dialog);
        }

        UpdateAddForm();
        UpdateReplaceForm();
        addSheet.Open = model.Adding;
        replaceDialog.Open = model.ReplacingHeader is not null;
        body.Add(addSheet);
        body.Add(replaceDialog);
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
        empty.Action += (_, _) => { draft = new(); model.StartAdding(); Refresh(); };
        return empty;
    }

    private void UpdateAddForm()
    {
        if (kind.Value != draft.AuthKind)
        {
            kind.Value = draft.AuthKind;
        }

        nameField.Value = draft.Name;
        addressField.Value = draft.Url;
        headerNameField.Value = draft.HeaderName;
        headerValueField.Value = draft.HeaderValue;
        headerPart.Visibility = draft.AuthKind is "header" ? Visibility.Visible : Visibility.Collapsed;
        oauthPart.Visibility = draft.AuthKind is "oauth" ? Visibility.Visible : Visibility.Collapsed;
        signInScope.Value = draft.SignInScope;
        scopeNote.Text = draft.SignInScope is "space"
            ? "Every bot run in the space acts as the one account an owner or admin signs in with."
            : "A bot run acts as the person it runs for, and can use this server once that person has signed in.";
        ownClient.Value = draft.OwnClient;
        clientPart.Visibility = draft.OwnClient ? Visibility.Visible : Visibility.Collapsed;
        noClientNote.Visibility = draft.OwnClient ? Visibility.Collapsed : Visibility.Visible;
        clientIdField.Value = draft.ClientId;
        clientSecretField.Value = draft.ClientSecret;
        issuerField.Value = draft.Issuer;
        trust.Value = draft.Trusted;
        protocol.Value = draft.ProtocolMode;
        addProblem.Text = model.AddProblem;
        addBusy.Text = model.AddingBusy ? "Adding the server." : string.Empty;
        addBusy.Visibility = model.AddingBusy ? Visibility.Visible : Visibility.Collapsed;
        cancelAdd.IsEnabled = !model.AddingBusy;
        addButton.IsEnabled = !model.AddingBusy;
    }

    private void UpdateReplaceForm()
    {
        if (model.ReplacingHeader is not { } connection)
        {
            replaceNameField.Value = string.Empty;
            replaceValueField.Value = string.Empty;
            return;
        }

        replaceDialog.Title = $"Replace the header for {connection.Name}";
        replaceNameField.Value = model.HeaderName;
        replaceValueField.Value = model.HeaderValue;
        replaceProblem.Text = model.Problems.GetValueOrDefault(connection.Id);
        replaceBusy.Text = model.ChangePending ? "Saving the header." : string.Empty;
        replaceBusy.Visibility = model.ChangePending ? Visibility.Visible : Visibility.Collapsed;
        cancelReplace.IsEnabled = !model.ChangePending;
        saveHeader.IsEnabled = !model.ChangePending;
    }

    private Drover.Contracts.Tools.ToolConnectionSummary Replacing() =>
        model.Connections!.First(connection => connection.Id == model.ReplacingHeader!.Id);

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

        var beneath = Forms.Column();
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
            var entries = Forms.Column();
            foreach (var tool in tools)
            {
                var entry = Forms.Column(Styles.Text(string.IsNullOrWhiteSpace(tool.Title) ? tool.Name : tool.Title, "DvTextH3"),
                    Styles.Text(tool.Name, "DvTextMono"), Styles.Text(tool.Description, "DvTextSecondary"));
                if (tool.State is "missing")
                {
                    entry.Children.Add(Styles.Text("The server no longer lists this tool, so no bot can use it.", "DvTextSecondary"));
                }

                if (tool.NeedsReview)
                {
                    ToolReview(entry, tool);
                    if (model.CanEdit)
                    {
                        entry.Children.Add(Action($"Accept the change to {(string.IsNullOrWhiteSpace(tool.Title) ? tool.Name : tool.Title)}",
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
        entry.Children.Add(Styles.Text("The server changed what this tool says it does. Bots that have it ask before each use until the change is accepted.", "DvTextSecondary"));
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

    private Menu CardMenu(Drover.Contracts.Tools.ToolConnectionSummary connection)
    {
        var menu = new Menu
        {
            Label = "More",
            AccessibleLabel = $"More for {connection.Name}",
            TriggerIcon = IconName.More,
            ShowTriggerLabel = false,
        };

        if (model.CanEdit)
        {
            menu.Items.Add(MenuEntry("Check and list tools", () => Run(() => model.Refresh(connection))));
            if (connection.AuthKind is "header")
            {
                menu.Items.Add(MenuEntry("Replace header", () => { model.StartReplacingHeader(connection); Refresh(); }));
            }
        }

        var maySignIn = connection.SignInScope is "per_user" || model.CanEdit;
        if (connection.SignIn is "signed_in" && maySignIn)
        {
            menu.Items.Add(MenuEntry($"Sign out of {connection.Name}", () => Run(() => model.SignOut(connection))));
        }

        if (model.CanEdit)
        {
            menu.Items.Add(MenuEntry($"Remove {connection.Name}", () => { model.Removing = connection; Refresh(); }));
        }

        return menu;
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

    private Field Field(string label, Action<string> change, string type = "text", string? hint = null)
    {
        var field = PageParts.Field(label, string.Empty, change, type, hint, model.ChangePending);
        return field;
    }

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

    private static MenuItem MenuEntry(string label, Action choose)
    {
        var item = new MenuItem { Content = label, HorizontalContentAlignment = HorizontalAlignment.Left };
        item.Selected += (_, _) => choose();
        return item;
    }

    #endregion
}
