namespace Drover.Native.Pages;

/// <summary>
/// One installed extension on the extensions screen: its name, what it adds, whether its bot is on, and the way to
/// set it up or turn it on, with its tool server's steps under it when it names one.
/// </summary>
internal sealed class ExtensionRow : Grid
{
    #region Private Fields

    private readonly ExtensionsViewModel model;

    private readonly Facet frame = new() { Cut = Tokens.Number("DvCut"), Padding = new(Tokens.Number("DvSpace4")) };

    private readonly StackPanel column = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly FlexPanel head = new() { Gap = Tokens.Number("DvSpace3"), CenterItems = false };

    private readonly Button turnOn = new() { Kind = ButtonKind.Primary };

    private ExtensionServerSteps? steps;

    private ExtensionSummary? shown;

    #endregion

    #region Constructors

    public ExtensionRow(ExtensionsViewModel model)
    {
        this.model = model;
        column.Children.Add(head);
        frame.Children.Add(column);
        Children.Add(frame);
        turnOn.Click += (_, _) => _ = model.TurnOn(shown!);
    }

    #endregion

    #region Properties

    /// <summary>The extension as it is after a step of its tool server, for the list to show.</summary>
    public event Action<ExtensionSummary>? Changed;

    #endregion

    #region Public Methods

    /// <summary>Shows the extension. The same one again changes nothing.</summary>
    public void Show(ExtensionSummary extension)
    {
        if (ReferenceEquals(shown, extension))
        {
            return;
        }

        shown = extension;
        head.Children.Clear();
        head.Children.Add(Text(extension));
        head.Children.Add(Actions(extension));
        ShowServer(extension);
        Refresh();
    }

    /// <summary>Shows what depends on what the screen is doing: the turn on button says it is working.</summary>
    public void Refresh()
    {
        if (shown is null || shown.BotId is null)
        {
            return;
        }

        turnOn.Content = model.TurnOnLabel(shown);
        turnOn.IsEnabled = model.Busy != shown.Id;
    }

    #endregion

    #region Private Methods

    private StackPanel Text(ExtensionSummary extension)
    {
        var text = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
        FlexPanel.SetBasis(text, Tokens.Number("DvColumnMin"));
        FlexPanel.SetGrow(text, 1);
        text.Children.Add(Parts.Heading(extension.Name, "DvTextH3", AutomationHeadingLevel.Level2));

        if (!string.IsNullOrWhiteSpace(extension.Description))
        {
            text.Children.Add(Parts.Small(extension.Description));
        }

        text.Children.Add(Parts.Small(ExtensionWords.Adds(extension)));
        return text;
    }

    private FlexPanel Actions(ExtensionSummary extension)
    {
        var actions = new FlexPanel { Gap = Tokens.Number("DvSpace3"), RowGap = Tokens.Number("DvSpace2") };
        (turnOn.Parent as Panel)?.Children.Remove(turnOn);

        if (extension.BotId is { } botId)
        {
            actions.Children.Add(new Badge { Text = ExtensionWords.Status(extension), Kind = ExtensionsViewModel.StatusKind(extension) });
            actions.Children.Add(Links.Plain(ExtensionsViewModel.SetUpLabel(extension), Addresses.Bot(botId)));

            if (model.ShowsTurnOn(extension))
            {
                actions.Children.Add(turnOn);
            }
        }

        return actions;
    }

    private void ShowServer(ExtensionSummary extension)
    {
        if (extension.Connection is null)
        {
            return;
        }

        if (steps is null)
        {
            steps = new();
            steps.Changed += changed => Changed?.Invoke(changed);
            column.Children.Add(steps);
        }

        steps.Show(extension);
    }

    #endregion
}
