namespace Drover.Presentation.ViewModels;

/// <summary>
/// Asks the person, each time the app starts, to remove the retired app while it is still installed. Its icon looks
/// like this one's, so a person can open it without knowing, and nothing they change there reaches this app.
/// </summary>
public sealed partial class RetiredAppViewModel(IRetiredApp? retired = null) : ObservableObject
{
    #region Private Fields

    [ObservableProperty]
    private bool open;

    #endregion

    #region Properties

    public static string Title => "Remove the old Drover app";

    public static string Sentence => $"{IRetiredApp.Name} is the old app. It no longer updates, and what you change "
        + "there does not reach this app. Uninstall it so its icon does not open it by mistake.";

    public static string RemoveLabel => $"Uninstall {IRetiredApp.Name}";

    public static string LaterLabel => "Not now";

    #endregion

    #region Public Methods

    /// <summary>Opens the request when the retired app is on this device.</summary>
    public void Check() => Open = retired?.Installed == true;

    /// <summary>Takes the person to the retired app's system page, where they uninstall it.</summary>
    public void Remove()
    {
        Open = false;
        retired?.OpenSettings();
    }

    /// <summary>Closes the request until the app next starts.</summary>
    public void Later() => Open = false;

    #endregion
}
