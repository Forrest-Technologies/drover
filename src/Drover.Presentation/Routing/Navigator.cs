namespace Drover.Presentation.Routing;

/// <summary>
/// Where the app is, and the addresses it came through. Screens ask it to go somewhere; the shell listens and shows
/// the screen for the address. In a browser the shell also keeps the address bar and the browser's own history in
/// step, in both directions, so every address is a link that can be opened, shared, and gone back from.
/// </summary>
public sealed class Navigator
{
    #region Private Fields

    private readonly List<string> history = [Addresses.Home];

    private int position;

    #endregion

    #region Properties

    public string Current => history[position];

    public bool CanGoBack => position > 0;

    public bool CanGoForward => position < history.Count - 1;

    /// <summary>Raised after the address changed, with how it changed.</summary>
    public event Action<string, NavigationKind>? Navigated;

    #endregion

    #region Public Methods

    /// <summary>Goes to an address. With <paramref name="replace"/>, the address left behind cannot be gone back to.</summary>
    public void NavigateTo(string address, bool replace = false)
    {
        if (replace)
        {
            history[position] = address;
        }
        else
        {
            history.RemoveRange(position + 1, history.Count - position - 1);
            history.Add(address);
            position++;
        }

        Navigated?.Invoke(address, replace ? NavigationKind.Replace : NavigationKind.Push);
    }

    public bool GoBack() => Travel(-1);

    public bool GoForward() => Travel(1);

    /// <summary>
    /// The address the app was opened at, before anything is shown. It becomes the first and only entry.
    /// </summary>
    public void StartAt(string address)
    {
        history.Clear();
        history.Add(address);
        position = 0;
    }

    /// <summary>
    /// The browser moved through its own history to an address. The app follows: to the entry before or after when
    /// the address is there, and otherwise it takes the address as where it now is.
    /// </summary>
    public void Arrive(string address)
    {
        if (CanGoBack && history[position - 1] == address)
        {
            position--;
        }
        else if (CanGoForward && history[position + 1] == address)
        {
            position++;
        }
        else
        {
            history[position] = address;
        }

        Navigated?.Invoke(address, NavigationKind.Travel);
    }

    #endregion

    #region Private Methods

    private bool Travel(int step)
    {
        var target = position + step;
        if (target < 0 || target >= history.Count)
        {
            return false;
        }

        position = target;
        Navigated?.Invoke(Current, NavigationKind.Travel);
        return true;
    }

    #endregion
}
