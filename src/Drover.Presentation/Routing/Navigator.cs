namespace Drover.Presentation.Routing;

/// <summary>
/// Where the app is, and the addresses it came through. Screens ask it to go somewhere; the shell listens and shows
/// the screen for the address. In a browser the shell also keeps the address bar and the browser's own history in
/// step, in both directions, so every address is a link that can be opened, shared, and gone back from. A device's
/// Back takes one step at a time: it closes what is open over the screen before it leaves the screen.
/// </summary>
public sealed class Navigator
{
    #region Private Fields

    private readonly List<string> history = [Addresses.Home];

    // What is open over the screen, the last opened at the end. A step back closes the last one first.
    private readonly List<Action> closers = [];

    private int position;

    #endregion

    #region Properties

    public string Current => history[position];

    public bool CanGoBack => position > 0;

    /// <summary>The address a step back goes to, or null at the first entry.</summary>
    public string? Previous => CanGoBack ? history[position - 1] : null;

    public bool CanGoForward => position < history.Count - 1;

    /// <summary>True when a step back stays in the app: something is open to close, or there is an address to go back to.</summary>
    public bool CanStepBack => closers.Count > 0 || CanGoBack;

    /// <summary>Raised when <see cref="CanStepBack"/> may have changed.</summary>
    public event Action? StepBackChanged;

    /// <summary>Raised after the address changed, with how it changed.</summary>
    public event Action<string, NavigationKind>? Navigated;

    /// <summary>
    /// Asked before the app goes to another address, with that address. A screen holding work that would be lost
    /// sets it, and answers false to stay where it is and ask the person first.
    /// </summary>
    public Func<string, bool>? Leaving { get; set; }

    #endregion

    #region Public Methods

    /// <summary>Goes to an address. With <paramref name="replace"/>, the address left behind cannot be gone back to.</summary>
    public void NavigateTo(string address, bool replace = false)
    {
        if (Leaving is { } ask && !ask(address))
        {
            return;
        }

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

        Announce(address, replace ? NavigationKind.Replace : NavigationKind.Push);
    }

    public bool GoBack() => Travel(-1);

    public bool GoForward() => Travel(1);

    /// <summary>
    /// One step back, as a device's Back asks for it. The last thing opened over the screen closes first. Then the app
    /// goes to the address before, unless the screen holds work and asks the person first. With nothing to close and
    /// nowhere to go back to, the step leaves the app.
    /// </summary>
    public BackStep StepBack()
    {
        if (closers is [.., var close])
        {
            closers.RemoveAt(closers.Count - 1);
            close();
            StepBackChanged?.Invoke();
            return BackStep.Closed;
        }

        if (!CanGoBack)
        {
            return BackStep.Out;
        }

        return GoBack() ? BackStep.WentBack : BackStep.Held;
    }

    /// <summary>
    /// Something is open over the screen, such as a dialog or a rail, and <paramref name="close"/> closes it. A step
    /// back closes it before the app leaves the screen. Adding the same one again changes nothing.
    /// </summary>
    public void AddCloser(Action close)
    {
        if (!closers.Contains(close))
        {
            closers.Add(close);
            StepBackChanged?.Invoke();
        }
    }

    /// <summary>What <paramref name="close"/> closes is gone, so a step back no longer closes it.</summary>
    public void RemoveCloser(Action close)
    {
        if (closers.Remove(close))
        {
            StepBackChanged?.Invoke();
        }
    }

    /// <summary>
    /// Goes back to <paramref name="address"/> when it is the entry before, and otherwise takes it in place of where
    /// the app is. Closing a part of a screen this way leaves no step behind to open it again.
    /// </summary>
    public void ReturnTo(string address)
    {
        if (Previous == address)
        {
            GoBack();
        }
        else
        {
            NavigateTo(address, replace: true);
        }
    }

    /// <summary>
    /// The address the app was opened at, before anything is shown. It becomes the first and only entry.
    /// </summary>
    public void StartAt(string address)
    {
        history.Clear();
        history.Add(address);
        position = 0;
        StepBackChanged?.Invoke();
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

        Announce(address, NavigationKind.Travel);
    }

    #endregion

    #region Private Methods

    private bool Travel(int step)
    {
        var target = position + step;
        if (target < 0 || target >= history.Count || (Leaving is { } ask && !ask(history[target])))
        {
            return false;
        }

        position = target;
        Announce(Current, NavigationKind.Travel);
        return true;
    }

    private void Announce(string address, NavigationKind kind)
    {
        Navigated?.Invoke(address, kind);
        StepBackChanged?.Invoke();
    }

    #endregion
}
