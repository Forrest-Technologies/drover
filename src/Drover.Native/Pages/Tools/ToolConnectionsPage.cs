namespace Drover.Native.Pages;

/// <summary>Add and inspect tool servers, sign in, review catalog changes, and remove connections.</summary>
public sealed class ToolConnectionsPage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly ToolConnectionsViewModel model = Make<ToolConnectionsViewModel>();

    private Drover.Ui.Tools.ConnectionDraft draft = new();

    private Guid? replacing;

    private string headerName = "Authorization";

    private string headerValue = string.Empty;

    #endregion

    #region Constructors

    public ToolConnectionsPage()
    {
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
        body.Add(new ScreenHeader { Title = "Tool connections" });
        if (model.CanEdit && model.Connections is { Count: > 0 } && !model.Adding)
        {
            var add = Action("Add a tool server", StartAdding, ButtonKind.Primary);
            add.Content = PageParts.WithIcon(IconName.Plus, "Add a tool server");
            body.Add(add);
        }
        body.Add(new StatusNote { Text = model.Notice, Kind = model.NoticeIsProblem ? NoteKind.Danger : NoteKind.Ok });

        if (model.Adding)
        {
            body.Add(AddForm());
        }

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
        else if (model.Connections.Count == 0 && !model.Adding)
        {
            if (model.CanEdit)
            {
                var empty = new EmptyState
                {
                    Title = "No tool server yet",
                    Cause = "A tool server gives bots tools beyond the built-in ones. Add one by its address, then give its tools to a bot.",
                    ActionLabel = "Add a tool server",
                };
                empty.Action += (_, _) => { StartAdding(); Refresh(); };
                body.Add(empty);
            }
            else
            {
                body.Add(Styles.Text("This space has no tool servers yet. An owner or admin of the space can add one.", "DvTextSecondary"));
            }
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
    }

    private void StartAdding()
    {
        draft = new();
        model.StartAdding();
    }

    private Facet AddForm()
    {
        var kind = new SegmentedControl
        {
            Label = "How Drover proves who is calling",
            Options = [new("none", "Nothing"), new("header", "A header"), new("oauth", "A sign-in")],
            Value = draft.AuthKind,
            Disabled = model.AddingBusy,
        };
        kind.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) => { draft.AuthKind = kind.Value ?? "none"; Refresh(); });
        var name = Field("Name", draft.Name, value => draft.Name = value, hint: "What you call it, such as Calendar.");
        name.MaxLength = FieldLimits.ToolServerName;
        var address = Field("Address", draft.Url, value => draft.Url = value, "url", "The address of the server's MCP endpoint.");
        address.Placeholder = "https://";

        // The choice's name is drawn above it as the web form draws it; assistive technology reads it from the choice.
        var kindName = Styles.Text(kind.Label, "DvTextLabel");
        AutomationProperties.SetAccessibilityView(kindName, AccessibilityView.Raw);
        var fields = PageParts.Column(PageParts.CardTitle("Add a tool server"),
            name,
            address, new StackPanel { Spacing = Tokens.Number("DvSpace2"), Children = { kindName, kind } });
        if (draft.AuthKind is "header")
        {
            fields.Children.Add(Field("Header name", draft.HeaderName, value => draft.HeaderName = value));
            fields.Children.Add(Field("Header value", draft.HeaderValue, value => draft.HeaderValue = value, "password", "Stored encrypted. It is not shown again."));
        }
        else if (draft.AuthKind is "oauth")
        {
            fields.Children.Add(Choice("Whose sign-in", draft.SignInScope,
                [new("per_user", "Each person signs in as themselves"), new("space", "One sign-in for the whole space")], value => draft.SignInScope = value));
            fields.Children.Add(Styles.Text(draft.SignInScope is "space"
                ? "Every bot run in the space acts as the one account an owner or admin signs in with."
                : "A bot run acts as the person it runs for, and can use this server once that person has signed in.", "DvTextSecondary"));
            fields.Children.Add(Toggle("Use a client I registered with the server", draft.OwnClient, value => draft.OwnClient = value));
            if (draft.OwnClient)
            {
                fields.Children.Add(Field("Client id", draft.ClientId, value => draft.ClientId = value));
                fields.Children.Add(Field("Client secret", draft.ClientSecret, value => draft.ClientSecret = value, "password", "Leave empty for a public client. It is not shown again."));
                fields.Children.Add(Field("Sign-in service address", draft.Issuer, value => draft.Issuer = value, "url", "The service that gave out the client id. The id is sent nowhere else."));
            }
            else
            {
                fields.Children.Add(Styles.Text("Without one, Drover introduces itself to the server when someone first signs in.", "DvTextSecondary"));
            }
        }

        fields.Children.Add(new Disclosure
        {
            Title = "More choices",
            Body = PageParts.Column(Toggle("Trust what this server's tools return", draft.Trusted, value => draft.Trusted = value),
                Styles.Text("Leave this off unless you run the server. A result from a server that is not trusted is recorded on the run as untrusted text. It does not change what any bot may do.", "DvTextSecondary"),
                Choice("Protocol", draft.ProtocolMode, [new("automatic", "Chosen by Drover"), new("stateless_only", "Without sessions only"), new("session_only", "With sessions only")], value => draft.ProtocolMode = value)),
        });
        fields.Children.Add(new StatusNote { Text = model.AddProblem, Kind = NoteKind.Danger });
        var actions = new FlexPanel { Gap = Tokens.Number("DvSpace2"), RowGap = Tokens.Number("DvSpace2"), Justify = FlexJustify.End };
        actions.Children.Add(Action("Cancel", () => { model.Adding = false; draft = new(); }));
        actions.Children.Add(Action(model.AddingBusy ? "Adding the server" : "Add server", async () =>
        {
            await model.Add(draft);
            if (!model.Adding)
            { draft = new(); }
        }, ButtonKind.Primary));
        fields.Children.Add(actions);
        return PageParts.Card(fields);
    }

    private Facet Connection(Drover.Contracts.Tools.ToolConnectionSummary connection)
    {
        var status = Drover.Ui.Tools.ConnectionWords.Status(connection);
        var tools = model.Tools.GetValueOrDefault(connection.Id);
        var head = new FlexPanel { Gap = Tokens.Number("DvSpace3"), Justify = FlexJustify.SpaceBetween, CenterItems = false };
        head.Children.Add(PageParts.CardTitle(connection.Name));
        head.Children.Add(new Badge { Text = status.Words, Kind = status.Kind });
        var body = PageParts.Column(head,
            Styles.Text(connection.Url, "DvTextMono"), Styles.Text(Drover.Ui.Tools.ConnectionWords.Facts(connection, tools), "DvTextMono"));
        var shownError = connection.Status is "failed" ? connection.LastError : null;
        body.Children.Add(new StatusNote { Text = shownError, Kind = NoteKind.Danger });
        body.Children.Add(new StatusNote { Text = model.Problems.GetValueOrDefault(connection.Id) is { } problem && problem != shownError ? problem : null, Kind = NoteKind.Danger });
        var maySignIn = connection.SignInScope is "per_user" || model.CanEdit;
        if (Drover.Ui.Tools.ConnectionWords.SignInNeed(connection) is { } need)
        {
            body.Children.Add(Styles.Text(need));
            body.Children.Add(maySignIn
                ? Action(connection.SignIn is "needs_reconnect" ? "Sign in again" : "Sign in", () => model.SignIn(connection, PlatformShell.OpenOutside, ReturnScheme), ButtonKind.Primary)
                : Styles.Text("An owner or admin of the space signs in for everyone.", "DvTextSecondary"));
        }

        if (tools is { Count: > 0 })
        {
            var entries = PageParts.Column();
            foreach (var tool in tools)
            {
                var entry = PageParts.Column(Styles.Text(string.IsNullOrWhiteSpace(tool.Title) ? tool.Name : tool.Title, "DvTextH3"),
                    Styles.Text(tool.Name, "DvTextMono"), Styles.Text(tool.Description, "DvTextSecondary"));
                if (tool.State is "missing")
                {
                    entry.Children.Add(Styles.Text("The server no longer lists this tool, so no bot can use it.", "DvTextSecondary"));
                }

                if (tool.NeedsReview)
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

                    if (model.CanEdit)
                    {
                        entry.Children.Add(Action($"Accept the change to {(string.IsNullOrWhiteSpace(tool.Title) ? tool.Name : tool.Title)}", () => model.Accept(tool)));
                    }
                }

                entries.Children.Add(entry);
            }

            body.Children.Add(new Disclosure { Title = "Tools", Open = tools.Any(tool => tool.NeedsReview), Body = entries });
        }
        else if (tools is { Count: 0 } && connection.Status is "ready")
        {
            body.Children.Add(Styles.Text("The server answers and lists no tools.", "DvTextSecondary"));
        }

        if (model.CanEdit)
        {
            if (replacing == connection.Id)
            {
                body.Children.Add(Styles.Text("Replace the header", "DvTextH3"));
                body.Children.Add(Field("Header name", headerName, value => headerName = value));
                body.Children.Add(Field("Header value", headerValue, value => headerValue = value, "password", "Stored encrypted. It is not shown again."));
                body.Children.Add(PageParts.Row(Action("Cancel", StopReplacing), Action("Save header", async () =>
                {
                    await model.ReplaceHeader(connection, new(headerName.Trim(), headerValue));
                    StopReplacing();
                }, ButtonKind.Primary)));
            }

            body.Children.Add(Action(model.Busy == connection.Id ? "Working" : "Check and list tools", () => model.Refresh(connection)));
            if (connection.AuthKind is "header" && replacing != connection.Id)
            {
                body.Children.Add(Action("Replace header", () => replacing = connection.Id));
            }

            body.Children.Add(Action($"Remove {connection.Name}", () => model.Removing = connection, ButtonKind.Danger));
        }

        if (connection.SignIn is "signed_in" && maySignIn)
        {
            body.Children.Add(Action($"Sign out of {connection.Name}", () => model.SignOut(connection)));
        }

        return PageParts.Card(body);
    }

    private void StopReplacing() => (replacing, headerValue) = (null, string.Empty);

    private Field Field(string label, string value, Action<string> change, string type = "text", string? hint = null) =>
        PageParts.Field(label, value, change, type, hint, model.ChangePending);

    private Select Choice(string label, string value, IReadOnlyList<SelectOption> options, Action<string> change)
    {
        var select = new Select { Label = label, Options = options, Value = value, Disabled = model.ChangePending };
        select.RegisterPropertyChangedCallback(Select.ValueProperty, (_, _) => { change(select.Value); Refresh(); });
        return select;
    }

    private Switch Toggle(string label, bool value, Action<bool> change)
    {
        var toggle = new Switch { Label = label, Value = value, IsEnabled = !model.ChangePending };
        toggle.RegisterPropertyChangedCallback(Switch.ValueProperty, (_, _) => { change(toggle.Value); Refresh(); });
        return toggle;
    }

    private Button Action(string label, Action work, ButtonKind kind = ButtonKind.Quiet) => Action(label, () => { work(); return Task.CompletedTask; }, kind);

    private Button Action(string label, Func<Task> work, ButtonKind kind = ButtonKind.Quiet) => PageParts.Action(label, work, Refresh, !model.ChangePending, kind);

    #endregion
}
