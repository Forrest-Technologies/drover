namespace Drover.Native.Pages;

/// <summary>
/// The tool server of an installed extension: connect it, then choose which of its suggested tools the bot gets.
/// What it shows follows the view model, and is made again only when the extension it shows changes, so a value
/// being typed in one row is never lost to a change in another.
/// </summary>
internal sealed class ExtensionServerSteps : Grid
{
    #region Private Fields

    private readonly ExtensionServerStepsViewModel model =
        ActivatorUtilities.CreateInstance<ExtensionServerStepsViewModel>(((App)Application.Current).Services);

    private readonly Border rule = new();

    private readonly StackPanel column = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly List<ToolTick> ticks = [];

    private ExtensionSummary? shown;

    private Field? headerField;

    private Switch? localSwitch;

    private StatusNote? problem;

    private Button? connect;

    private Button? give;

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

    /// <summary>Shows the extension's tool server as it now is. The same extension again changes nothing.</summary>
    public void Show(ExtensionSummary extension)
    {
        if (ReferenceEquals(shown, extension))
        {
            return;
        }

        // A change that leaves the tool server alone (the bot turned on) must not clear what is being typed.
        var same = shown is not null && ReferenceEquals(shown.Connection, extension.Connection)
            && shown.BotId == extension.BotId && shown.BotName == extension.BotName;
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
        ticks.Clear();
        (headerField, localSwitch, problem, connect, give) = (null, null, null, null, null);

        var heading = Parts.Title(model.Heading, AutomationHeadingLevel.Level3);
        AutomationProperties.SetName(rule, model.Heading);
        column.Children.Add(heading);
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

        if (model.Connection.Auth is "header")
        {
            headerField = new Field { Label = model.HeaderValueLabel, Type = "password", Hint = "Stored encrypted. It is not shown again.", Value = model.HeaderValue };
            headerField.RegisterPropertyChangedCallback(Field.ValueProperty, (_, _) => model.HeaderValue = headerField.Value);
            headerField.Submitted += (_, _) => Connect();
            column.Children.Add(headerField);
        }

        if (model.Connection.IsLocal)
        {
            localSwitch = new Switch { Label = model.LocalLabel, Value = model.AcceptLocal };
            localSwitch.RegisterPropertyChangedCallback(Switch.ValueProperty, (_, _) => model.AcceptLocal = localSwitch.Value);
            column.Children.Add(localSwitch);
        }

        problem = new StatusNote { Kind = NoteKind.Danger };
        connect = new Button { Kind = ButtonKind.Quiet, HorizontalAlignment = HorizontalAlignment.Left };
        connect.Click += (_, _) => Connect();
        column.Children.Add(problem);
        column.Children.Add(connect);
    }

    private void AddConnected()
    {
        var state = new FlexPanel { Gap = Tokens.Number("DvSpace3"), RowGap = Tokens.Number("DvSpace2") };
        state.Children.Add(new Badge { Text = model.StatusText, Kind = model.StatusIsReady ? BadgeKind.Ok : BadgeKind.Warn });
        state.Children.Add(Links.Plain(model.LinkText, Addresses.Tools));
        column.Children.Add(state);

        if (!string.IsNullOrWhiteSpace(model.Connection.Problem))
        {
            column.Children.Add(Parts.Small(model.Connection.Problem));
        }

        if (!model.ShowsTools)
        {
            return;
        }

        var tools = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        AutomationProperties.SetName(tools, model.ToolsLegend);
        tools.Children.Add(Parts.Title(model.ToolsLegend, AutomationHeadingLevel.None));
        foreach (var tool in model.Connection.Tools)
        {
            tools.Children.Add(Tick(tool));
        }

        problem = new StatusNote { Kind = NoteKind.Danger };
        column.Children.Add(tools);
        column.Children.Add(problem);

        if (model.ShowsGive)
        {
            give = new Button { HorizontalAlignment = HorizontalAlignment.Left };
            give.Click += (_, _) => _ = model.GrantCommand.ExecuteAsync(null);
            column.Children.Add(give);
        }
    }

    private ToolTick Tick(SuggestedToolState tool)
    {
        var tick = new ToolTick(tool.Name, tool.Why, ExtensionWords.ToolState(tool)) { IsChecked = model.IsOn(tool), Tag = tool };
        tick.Checked += (_, _) => model.Tick(tool.Name, on: true);
        tick.Unchecked += (_, _) => model.Tick(tool.Name, on: false);
        ticks.Add(tick);
        return tick;
    }

    private void Connect() => _ = model.ConnectCommand.ExecuteAsync(null);

    // What changes while the row stands: the working state, the sentence of a failure, and what can be pressed.
    private void Sync()
    {
        if (shown is null)
        {
            return;
        }

        headerField?.Disabled = model.Busy;
        localSwitch?.IsEnabled = !model.Busy;
        problem?.Text = model.Problem;

        if (connect is not null)
        {
            connect.Content = model.ConnectLabel;
            connect.IsEnabled = !model.Busy;
        }

        if (give is not null)
        {
            give.Content = new TextBlock { Text = model.GiveLabel, TextWrapping = TextWrapping.Wrap };
            give.IsEnabled = model.CanGive;
        }

        foreach (var tick in ticks)
        {
            tick.IsEnabled = model.CanEdit && !model.Busy && tick.Tag is SuggestedToolState tool && ExtensionServerStepsViewModel.CanTick(tool);
        }
    }

    #endregion
}
