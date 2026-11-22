namespace Drover.Presentation.ViewModels;

/// <summary>
/// The theme a person had when they came to the Appearance page, so the page can offer it back after they choose
/// another. Choosing a theme builds the open page again, so this outlives the page; it is forgotten when the person
/// goes anywhere else.
/// </summary>
public sealed class AppearanceTrail : IDisposable
{
    #region Private Fields

    private readonly Navigator navigator;

    #endregion

    #region Constructors

    // A traditional constructor, because the trail listens to the navigator from the moment it exists.
#pragma warning disable IDE0290
    public AppearanceTrail(Navigator navigator)
#pragma warning restore IDE0290
    {
        this.navigator = navigator;
        navigator.Navigated += Navigated;
    }

    #endregion

    #region Properties

    /// <summary>The theme in use when the person first chose another on this visit to the page.</summary>
    public ThemeChoice? Before { get; private set; }

    #endregion

    #region Public Methods

    /// <summary>Notes the theme in use before a choice. Only the first choice of a visit is noted.</summary>
    public void Leaving(ThemeChoice current) => Before ??= current;

    public void Dispose() => navigator.Navigated -= Navigated;

    #endregion

    #region Private Methods

    private void Navigated(string address, NavigationKind kind)
    {
        if (!address.StartsWith(Addresses.Appearance, StringComparison.Ordinal))
        {
            Before = null;
        }
    }

    #endregion
}
