namespace Drover.App;

public sealed class ServerAddress
{
    #region Private Fields

    private const string Preference = "drover.server";

    #endregion

    #region Properties

    public Uri? Current { get; private set; } = Parse(Preferences.Default.Get(Preference, string.Empty));

    #endregion

    #region Public Methods

    public bool Set(string address)
    {
        var origin = Parse(address);
        if (origin is null)
        {
            return false;
        }

        Preferences.Default.Set(Preference, origin.AbsoluteUri);
        Current = origin;
        return true;
    }

    #endregion

    #region Private Methods

    private static Uri? Parse(string address) =>
        Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Length > 0
            && uri.UserInfo.Length == 0
            && uri.PathAndQuery == "/"
            && uri.Fragment.Length == 0
        ? uri
        : null;

    #endregion
}
