namespace Drover.Transport;

public sealed class ServerAddress
{
    #region Private Fields

    private const string Preference = "drover.server";

    #endregion

    #region Properties

    public Uri? Current { get; private set; } = Parse(Preferences.Default.Get(Preference, string.Empty));

    #endregion

    #region Public Methods

    public void Set(Uri origin)
    {
        Preferences.Default.Set(Preference, origin.AbsoluteUri);
        Current = origin;
    }

    /// <summary>Points at an origin for this run only. Nothing is stored.</summary>
    internal void Use(Uri origin) => Current = origin;

    public void Forget()
    {
        Preferences.Default.Remove(Preference);
        Current = null;
    }

    public static Uri? Parse(string address) =>
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
