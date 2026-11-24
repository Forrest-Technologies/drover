namespace Drover.Native.Pages;

/// <summary>
/// One installed extension on the extensions screen: the card holds its name, what it adds and whether its bot is
/// on. The way to set it up or turn it on stays in words beneath the card, as does its connection's steps.
/// </summary>
internal sealed class ExtensionRow : Grid
{
    #region Private Fields

    private readonly ExtensionsViewModel model;

    private readonly Navigator navigator = ((App)Application.Current).Services.GetRequiredService<Navigator>();

    private readonly ListCard card = new();

    private readonly FlexPanel beneath = new() { Gap = Tokens.Number("DvSpace3"), RowGap = Tokens.Number("DvSpace2") };

    private readonly Button turnOn = new() { Kind = ButtonKind.Primary };

    private ExtensionServerSteps? steps;

    private ExtensionSummary? shown;

    #endregion

    #region Constructors

    public ExtensionRow(ExtensionsViewModel model)
    {
        this.model = model;
        card.Beneath = beneath;
        Children.Add(card);
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
        card.Title = extension.Name;
        card.Facts = ExtensionWords.Adds(extension);
        card.Status = extension.BotId is null
            ? null
            : new Badge { Text = ExtensionWords.Status(extension), Kind = ExtensionsViewModel.StatusKind(extension) };
        beneath.Children.Clear();
        beneath.Children.Add(Actions(extension));
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

    private FlexPanel Actions(ExtensionSummary extension)
    {
        var actions = new FlexPanel { Gap = Tokens.Number("DvSpace3"), RowGap = Tokens.Number("DvSpace2") };
        (turnOn.Parent as Panel)?.Children.Remove(turnOn);

        if (extension.BotId is { } botId)
        {
            actions.Children.Add(BotParts.Action(
                ExtensionsViewModel.SetUpLabel(extension),
                () => navigator.NavigateTo(Addresses.Bot(botId))));

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
        }

        beneath.Children.Add(steps);
        steps.Show(extension);
    }

    #endregion
}
