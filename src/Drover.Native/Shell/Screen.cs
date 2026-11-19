namespace Drover.Native.Shell;

/// <summary>
/// A screen of the app: one page at one address. It says its title, makes its view model from the app's services,
/// and is told the address it was opened at, with the parameters and the query in it.
/// </summary>
public abstract class Screen : Page
{
    #region Properties

    /// <summary>The words in the window's title bar, or the browser tab: "Sign in to Drover".</summary>
    public abstract string Title { get; }

    /// <summary>The address this screen was opened at, taken apart. Set before <see cref="Opened"/> runs.</summary>
    public RouteMatch<Type>? Route { get; private set; }

    /// <summary>
    /// True for a screen that stays as it is when only the query of its address changes, as a tab named in the
    /// address does: it is told the new address through <see cref="Opened"/> instead of being made again.
    /// </summary>
    protected virtual bool StaysOpenWhenQueryChanges => false;

    #endregion

    #region Public Methods

    /// <summary>
    /// Hands the screen a new address with the same path, when it asked to stay open for that. False when the
    /// address is another screen's, or this screen is made again for each address.
    /// </summary>
    public bool Reopen(RouteMatch<Type> route)
    {
        if (!StaysOpenWhenQueryChanges || Route is null || PathOf(Route.Address) != PathOf(route.Address))
        {
            return false;
        }

        Open(route);
        return true;
    }

    #endregion

    #region Private Methods

    private static string PathOf(string address) => address.Split('?', '#')[0];

    /// <summary>
    /// Makes a view model, filling its constructor from the app's services. This is the screen's one use of the
    /// service provider, in place of the injection a control made by the platform cannot have.
    /// </summary>
    protected static T Make<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() =>
        ActivatorUtilities.CreateInstance<T>(((App)Application.Current).Services);

    /// <summary>
    /// Runs when the screen is shown at an address: the place to read parameters and load what it shows. A request
    /// that fails on the way is caught, and the screen's gate says the server did not answer.
    /// </summary>
    protected virtual Task Opened(RouteMatch<Type> route) => Task.CompletedTask;

    // The platform calls a screen without waiting for it, so a failure that escaped from here would close the app.
    private async void Open(RouteMatch<Type> route)
    {
        Route = route;
        var session = ((App)Application.Current).Services.GetRequiredService<SessionState>();
        await ScreenOpen.At(session, () => Opened(route), Log.Factory.CreateLogger(GetType()));
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        UnreachableOpenCheck.Seen(GetType());
        if (e.Parameter is RouteMatch<Type> route)
        {
            Open(route);
        }
    }

    #endregion
}
