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

    #endregion

    #region Private Methods

    /// <summary>
    /// Makes a view model, filling its constructor from the app's services. This is the screen's one use of the
    /// service provider, in place of the injection a control made by the platform cannot have.
    /// </summary>
    protected static T Make<T>() => ActivatorUtilities.CreateInstance<T>(((App)Application.Current).Services);

    /// <summary>Runs when the screen is shown at an address: the place to read parameters and load what it shows.</summary>
    protected virtual void Opened(RouteMatch<Type> route)
    {
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is RouteMatch<Type> route)
        {
            Route = route;
            Opened(route);
        }
    }

    #endregion
}
