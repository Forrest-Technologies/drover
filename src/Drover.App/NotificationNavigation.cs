namespace Drover.App;

public sealed class NotificationNavigation
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
