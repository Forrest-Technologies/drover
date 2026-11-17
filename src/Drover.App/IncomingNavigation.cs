namespace Drover.App;

/// <summary>
/// Where Android asked the app to go from outside it: a tapped notification, or a browser coming back from a tool
/// server's sign-in page. The interface takes the address when it is ready to show it.
/// </summary>
public sealed class IncomingNavigation
{
    #region Private Fields

    private string? waiting;

    #endregion

    #region Properties

    public event Action? Changed;

    #endregion

    #region Public Methods

    public void Open(string path)
    {
        Interlocked.Exchange(ref waiting, path);
        Changed?.Invoke();
    }

    public string? Take() => Interlocked.Exchange(ref waiting, null);

    #endregion
}
