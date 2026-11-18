namespace Drover.Native.Platform;

/// <summary>
/// The page's local storage, under the names the web app always used, so a choice made in one client holds in the
/// other. A browser can refuse storage; the caller is told, and carries on without it.
/// </summary>
internal sealed class BrowserSettings : IDeviceSettings
{
    #region Interface Implementations

    public string? Read(string name) => Guarded(() => Browser.ReadStored(name));

    public void Write(string name, string value) => Guarded<object?>(() =>
    {
        Browser.Store(name, value);
        return null;
    });

    public void Remove(string name) => Guarded<object?>(() =>
    {
        Browser.Forget(name);
        return null;
    });

    #endregion

    #region Private Methods

    private static T Guarded<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (JSException exception)
        {
            throw new InvalidOperationException("The browser refused to use its local storage.", exception);
        }
    }

    #endregion
}
