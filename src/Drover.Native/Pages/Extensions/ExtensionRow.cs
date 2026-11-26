namespace Drover.Native.Pages;

/// <summary>
/// One installed extension on the extensions screen: the card holds its name and what it holds in one line. Beneath
/// it stand the steps of its tool server, the themes and rules it offers, and the way to remove it, in words.
/// </summary>
internal sealed class ExtensionRow : Grid
{
    #region Private Fields

    private readonly ExtensionsViewModel model;

    private readonly ListCard card = new();

    private readonly StackPanel beneath = new() { Spacing = Tokens.Number("DvSpace3") };

    private ExtensionServerSteps? steps;

    private ExtensionSummary? shown;

    #endregion

    #region Constructors

    public ExtensionRow(ExtensionsViewModel model)
    {
        this.model = model;
        Children.Add(card);
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
        card.Facts = ExtensionWords.Holds(extension);
        beneath.Children.Clear();

        if (!string.IsNullOrWhiteSpace(extension.Description))
        {
            beneath.Children.Add(Parts.Small(extension.Description));
        }

        ShowServer(extension);

        foreach (var offered in ExtensionReview.Offered(extension.Parts))
        {
            beneath.Children.Add(ExtensionReview.Ruled(offered));
        }

        if (model.CanEdit)
        {
            var remove = BotParts.Action(ExtensionsViewModel.RemoveAction, () => model.AskToRemove(extension));
            remove.HorizontalAlignment = HorizontalAlignment.Left;
            AutomationProperties.SetName(remove, $"Remove {extension.Name}");
            beneath.Children.Add(remove);
        }

        card.Beneath = beneath.Children.Count > 0 ? beneath : null;
    }

    #endregion

    #region Private Methods

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
