namespace Drover.Server.Health;

/// <summary>
/// Where the database is. The password is never a setting: it is read from the file named here.
/// </summary>
public sealed class DatabaseOptions
{
    #region Properties

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 5432;

    public string Database { get; set; } = "postgres";

    public string Username { get; set; } = "postgres";

    public string PasswordFile { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 3;

    #endregion
}
