namespace Drover.Native.Pages;

/// <summary>
/// The tool server of an installed extension: fill in the extension's settings and connect it. What it shows
/// follows the view model, and is made again only when the tool server it shows changes, so a value being typed in
/// one row is never lost to a change in another.
/// </summary>
internal sealed class ExtensionServerSteps : Grid
{
    #region Private Fields

    private readonly ExtensionServerStepsViewModel model =
        ActivatorUtilities.CreateInstance<ExtensionServerStepsViewModel>(((App)Application.Current).Services);

    private readonly Border rule = new();

    private readonly StackPanel column = new() { Spacing = Tokens.Number("DvSpace3") };

    // Each input of the connect form, as the way to hold it still while the connection is being made.
    private readonly List<Action<bool>> held = [];

    private ExtensionSummary? shown;

    private StatusNote? problem;

    private Button? connect;

    #endregion

    #region Constructors

    public ExtensionServerSteps()
    {
        rule.Child = column;
        rule.BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0);
        rule.Padding = new(0, Tokens.Number("DvSpace3"), 0, 0);
        Children.Add(rule);
        model.PropertyChanged += (_, _) => Sync();
        model.Changed += changed => Changed?.Invoke(changed);
        this.WhenShownOrRethemed(() => rule.BorderBrush = Tokens.Brush(this, "DvLine"));
    }

    #endregion

    #region Properties

    /// <summary>The extension as it is after a step, for the list to show.</summary>
    public event Action<ExtensionSummary>? Changed;

    #endregion

    #region Public Methods

    /// <summary>Shows the extension's tool server as it now is. The same tool server again changes nothing.</summary>
    public void Show(ExtensionSummary extension)
    {
        if (ReferenceEquals(shown, extension))
        {
            return;
        }

        // A change that leaves the tool server alone must not clear what is being typed.
        var same = shown is not null && ReferenceEquals(shown.Connection, extension.Connection);
        shown = extension;
        model.Show(extension);

        if (!same)
        {
            Render();
        }
    }

    #endregion

    #region Private Methods

    private void Render()
    {
        column.Children.Clear();
        held.Clear();
        (problem, connect) = (null, null);

        AutomationProperties.SetName(rule, model.Heading);
        column.Children.Add(Parts.Title(model.Heading, AutomationHeadingLevel.Level3));
        column.Children.Add(Parts.Machine(model.Connection.Host, "DvText20", "DvText"));

        switch (model.Shows)
        {
            case ServerStep.TurnedOff:
                column.Children.Add(Parts.Small("Tool connections are turned off on this server, so this tool server is not connected."));
                break;
            case ServerStep.NotConnectedYet:
                column.Children.Add(Parts.Small("Not connected yet. An owner or admin of the space can connect it."));
                break;
            case ServerStep.Connect:
                AddConnectForm();
                break;
            default:
                AddConnected();
                break;
        }

        Sync();
    }

    private void AddConnectForm()
    {
        column.Children.Add(Parts.Small(model.ConnectLead));

        foreach (var setting in model.Settings)
        {
            column.Children.Add(SettingInput(setting));
        }

        if (model.AsksForHeaderValue)
        {
            var header = new Field { Label = model.HeaderValueLabel, Type = "password", Hint = ExtensionServerStepsViewModel.HeaderValueHint, Value = model.HeaderValue };
            header.RegisterPropertyChangedCallback(Field.ValueProperty, (_, _) => model.HeaderValue = header.Value);
            header.Submitted += (_, _) => Connect();
            held.Add(busy => header.Disabled = busy);
            column.Children.Add(header);
        }

        if (model.AsksAboutLocal)
        {
            var local = new Switch { Label = model.LocalLabel, Value = model.AcceptLocal };
            local.RegisterPropertyChangedCallback(Switch.ValueProperty, (_, _) => model.AcceptLocal = local.Value);
            held.Add(busy => local.IsEnabled = !busy);
            column.Children.Add(local);
        }

        problem = new StatusNote { Kind = NoteKind.Danger };
        connect = new Button { Kind = ButtonKind.Quiet, HorizontalAlignment = HorizontalAlignment.Left };
        connect.Click += (_, _) => Connect();
        column.Children.Add(problem);
        column.Children.Add(connect);
    }

    // Each setting is the control its type calls for: a switch, a list to pick from, or a field. A secret is a password field.
    private UIElement SettingInput(ExtensionSetting setting)
    {
        switch (setting.Type)
        {
            case ExtensionSetting.Toggle:
                var toggle = new Switch { Label = setting.Label, Value = model.Value(setting) is "true" };
                toggle.RegisterPropertyChangedCallback(Switch.ValueProperty, (_, _) => model.SetValue(setting, toggle.Value ? "true" : "false"));
                held.Add(busy => toggle.IsEnabled = !busy);
                return string.IsNullOrWhiteSpace(setting.Help) ? toggle : BotParts.Column("DvSpace1", toggle, Parts.Small(setting.Help));
            case ExtensionSetting.Choice:
                var choice = new Select
                {
                    Label = setting.Label,
                    Options = [.. setting.Choices.Select(option => new SelectOption(option, option))],
                    Value = model.Value(setting) is { Length: > 0 } chosen ? chosen : setting.Choices[0],
                };
                model.SetValue(setting, choice.Value);
                choice.RegisterPropertyChangedCallback(Select.ValueProperty, (_, _) => model.SetValue(setting, choice.Value));
                held.Add(busy => choice.Disabled = busy);
                return string.IsNullOrWhiteSpace(setting.Help) ? choice : BotParts.Column("DvSpace1", choice, Parts.Small(setting.Help));
            default:
                var secret = setting.Type is ExtensionSetting.Secret;
                var field = new Field
                {
                    Label = setting.Label,
                    Type = secret ? "password" : "text",
                    Hint = ExtensionWords.SettingHint(setting) is { Length: > 0 } hint ? hint : null,
                    Value = model.Value(setting),
                };
                field.RegisterPropertyChangedCallback(Field.ValueProperty, (_, _) => model.SetValue(setting, field.Value));
                field.Submitted += (_, _) => Connect();
                held.Add(busy => field.Disabled = busy);
                return field;
        }
    }

    private void AddConnected()
    {
        var state = new FlexPanel { Gap = Tokens.Number("DvSpace3"), RowGap = Tokens.Number("DvSpace2") };
        state.Children.Add(new Badge { Text = model.StatusText, Kind = model.StatusIsReady ? BadgeKind.Ok : BadgeKind.Warn });
        state.Children.Add(Links.Plain(model.LinkText, Addresses.Tools));
        column.Children.Add(state);
        column.Children.Add(Parts.Fact("Full address", Parts.Machine(model.Connection.Url, "DvText12", "DvText3")));

        if (!string.IsNullOrWhiteSpace(model.Connection.Problem))
        {
            column.Children.Add(Parts.Small(model.Connection.Problem));
        }

        if (!model.ShowsTools)
        {
            return;
        }

        var tools = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        AutomationProperties.SetName(tools, ExtensionServerStepsViewModel.ToolsLegend);
        tools.Children.Add(Parts.Title(ExtensionServerStepsViewModel.ToolsLegend, AutomationHeadingLevel.None));
        tools.Children.Add(ExtensionReview.Lines(model.Connection.Tools.Select(Told), machine: true));
        tools.Children.Add(Parts.Small(ExtensionServerStepsViewModel.GrantHint));
        column.Children.Add(tools);
    }

    // What is said of a suggested tool: why the file suggests it, and that the server does not list it when it does not.
    private static (string Name, string About) Told(SuggestedToolState tool) =>
        (tool.Name, string.Join(' ', new[] { tool.Why, ExtensionWords.ToolState(tool) }.Where(said => !string.IsNullOrWhiteSpace(said))));

    private void Connect() => _ = model.ConnectCommand.ExecuteAsync(null);

    // What changes while the row stands: the working state, the sentence of a failure, and what can be pressed.
    private void Sync()
    {
        if (shown is null)
        {
            return;
        }

        foreach (var hold in held)
        {
            hold(model.Busy);
        }

        problem?.Text = model.Problem;

        if (connect is not null)
        {
            connect.Content = model.ConnectLabel;
            connect.IsEnabled = !model.Busy;
        }
    }

    #endregion
}
