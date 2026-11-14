namespace Drover.Server.Health;

/// <summary>
/// Database location and file-based settings, plus the resolved application connection used by readiness.
/// </summary>
public sealed class DatabaseOptions
{
    #region Properties

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 5432;

    public string Database { get; set; } = "postgres";

    public string Username { get; set; } = "drover_app";

    public string ConnectionString { get; set; } = string.Empty;

    public string PasswordFile { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 3;

    /// <summary>
    /// TLS to PostgreSQL. <c>Prefer</c> uses it when the server offers it and does not check the certificate;
    /// <c>Require</c> refuses a plain connection; <c>VerifyFull</c> also checks the certificate and the host name.
    /// </summary>
    public SslMode SslMode { get; set; } = SslMode.Prefer;

    #endregion
}
