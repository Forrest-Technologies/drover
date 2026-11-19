namespace Drover.Native.Checks;

/// <summary>
/// What the harness tells a build made for the screen checks: the test server, the account on it, the words its
/// bot answers with, and where to report. A desktop reads them from the environment; Android from the extras of the
/// intent that started the app. When they are missing the app starts as any other build does.
/// </summary>
internal sealed record CheckSettings(
    Uri Server,
    string Email,
    string Password,
    string Certificate,
    string Reply,
    string? ReportPath)
{
    #region Private Fields

    public const string ServerName = "DROVER_UI_CHECK_SERVER";

    public const string EmailName = "DROVER_UI_CHECK_EMAIL";

    public const string PasswordName = "DROVER_UI_CHECK_PASSWORD";

    public const string CertificateName = "DROVER_UI_CHECK_CERTIFICATE";

    public const string ReplyName = "DROVER_UI_CHECK_REPLY";

    public const string ReportName = "DROVER_UI_CHECK_REPORT";

    private static readonly Lazy<CheckSettings?> current = new(Read);

    #endregion

    #region Properties

    /// <summary>The settings this run was started with, or null when the app was not started for the checks.</summary>
    public static CheckSettings? Current => current.Value;

    #endregion

#if !DROVER_BROWSER
    #region Public Methods

    /// <summary>
    /// The connection to the test server. It accepts the one certificate the harness made for this run, by its
    /// SHA-256 hash, as well as any certificate the platform already trusts.
    /// </summary>
    public HttpMessageHandler Transport()
    {
        // The app's own handler is made the same way in AppRequests; only the certificate check is added.
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
            errors is System.Net.Security.SslPolicyErrors.None
            || string.Equals(
                certificate?.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256),
                Certificate,
                StringComparison.OrdinalIgnoreCase);
        return handler;
    }

    #endregion
#endif

    #region Private Methods

    private static CheckSettings? Read()
    {
        if (Value(ServerName) is not { } server || !Uri.TryCreate(server, UriKind.Absolute, out var origin)
            || Value(EmailName) is not { } email || Value(PasswordName) is not { } password
            || Value(CertificateName) is not { } certificate || Value(ReplyName) is not { } reply)
        {
            return null;
        }

        return new(origin, email, password, certificate, reply, Value(ReportName));
    }

    private static string? Value(string name)
    {
#if __ANDROID__
        var value = (Uno.UI.ContextHelper.Current as Android.App.Activity)?.Intent?.GetStringExtra(name);
#elif DROVER_BROWSER
        // The page is served by the test server. The harness puts the settings in local storage and installs the
        // function that reads them, before the app starts. A browser has no process environment to read.
        var value = PageCheckChannel.Read(name);
#else
        var value = Environment.GetEnvironmentVariable(name);
#endif
        return string.IsNullOrEmpty(value) ? null : value;
    }

    #endregion
}
