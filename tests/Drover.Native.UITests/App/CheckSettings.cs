namespace Drover.Native.Checks;

/// <summary>
/// What the harness tells a build made for the screen checks: the test server, the account on it, the project and bot
/// it opens, the words its bot answers with, and where to report. A desktop reads them from the environment; Android from the extras of the
/// intent that started the app. When they are missing the app starts as any other build does.
/// </summary>
internal sealed record CheckSettings(
    Uri Server,
    string Email,
    string Password,
    string Certificate,
    string Reply,
    string? ReportPath,
    string? RunId,
    string? ProofPicture = null,
    Guid? ProofBot = null,
    Guid? ProjectId = null)
{
    #region Private Fields

    public const string ServerName = "DROVER_UI_CHECK_SERVER";

    public const string EmailName = "DROVER_UI_CHECK_EMAIL";

    public const string PasswordName = "DROVER_UI_CHECK_PASSWORD";

    public const string CertificateName = "DROVER_UI_CHECK_CERTIFICATE";

    public const string ReplyName = "DROVER_UI_CHECK_REPLY";

    public const string ReportName = "DROVER_UI_CHECK_REPORT";

    public const string RunIdName = "DROVER_UI_CHECK_RUN";

    public const string RestartName = "DROVER_UI_CHECK_RESTART";

    public const string ProofPictureName = "DROVER_UI_CHECK_PROOF_PICTURE";

    public const string ProofBotName = "DROVER_UI_CHECK_PROOF_BOT";

    public const string ProjectName = "DROVER_UI_CHECK_PROJECT";

    private static readonly Lazy<CheckSettings?> current = new(Read);

    #endregion

    #region Properties

    /// <summary>The settings this run was started with, or null when the app was not started for the checks.</summary>
    public static CheckSettings? Current => current.Value;

    public bool ChecksRestart =>
        string.Equals(Value(RestartName), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>The manual Android proof asked for: the picture to attach and the bot whose sandbox to open.</summary>
    public bool IsProof => ProofPicture is not null && ProofBot is not null;

    #endregion

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

    #region Private Methods

    private static CheckSettings? Read()
    {
        if (Value(ServerName) is not { } server || !Uri.TryCreate(server, UriKind.Absolute, out var origin)
            || Value(EmailName) is not { } email || Value(PasswordName) is not { } password
            || Value(CertificateName) is not { } certificate || Value(ReplyName) is not { } reply)
        {
            return null;
        }

        var bot = Guid.TryParse(Value(ProofBotName), out var proofBot) ? proofBot : (Guid?)null;
        var project = Guid.TryParse(Value(ProjectName), out var checkProject) ? checkProject : (Guid?)null;
        return new(
            origin,
            email,
            password,
            certificate,
            reply,
            Value(ReportName),
            Value(RunIdName),
            Value(ProofPictureName),
            bot,
            project);
    }

    private static string? Value(string name)
    {
#if __ANDROID__
        var value = (Uno.UI.ContextHelper.Current as Android.App.Activity)?.Intent?.GetStringExtra(name);
#else
        var value = Environment.GetEnvironmentVariable(name);
#endif
        return string.IsNullOrEmpty(value) ? null : value;
    }

    #endregion
}
