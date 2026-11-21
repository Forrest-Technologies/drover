namespace Drover.Ui.Session;

/// <summary>An address to open and, when the system knows it, the space it is in.</summary>
public sealed record IncomingOpening(string Path, Guid? Space);

/// <summary>
/// Where Android asked the app to go from outside it: a tapped notification, or a browser coming back from a tool
/// server's sign-in page. The interface takes the address when it is ready to show it. A tapped notification also
/// names the space of its conversation, so the address is opened in that space and not only in the person's own.
/// </summary>
public sealed class IncomingNavigation
{
    #region Private Fields

    private IncomingOpening? waiting;

    #endregion

    #region Properties

    public event Action? Changed;

    #endregion

    #region Public Methods

    public void Open(string path, Guid? space = null)
    {
        Interlocked.Exchange(ref waiting, new IncomingOpening(path, space));
        Changed?.Invoke();
    }

    public string? Take() => Interlocked.Exchange(ref waiting, null)?.Path;

    /// <summary>The address waiting and the space it is in, taken together so neither is separated from the other.</summary>
    public IncomingOpening? TakeOpening() => Interlocked.Exchange(ref waiting, null);

    #endregion
}
