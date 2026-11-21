namespace Drover.Tests.Common;

/// <summary>
/// One password per role and PostgreSQL server, the same in every test assembly that runs against that server.
/// Roles belong to the server, not to one database or process. If each assembly chose its own password they would
/// reset one another's sign-ins when they run together, because only the last bootstrap's password would work.
/// Deriving the value from the server keeps every assembly on it in agreement, with no lease or queue; a throwaway
/// container has its own host and port, so each run there gets passwords nobody else uses.
/// </summary>
public static class TestRolePasswords
{
    #region Public Methods

    /// <summary>The password this run's fixtures give <paramref name="role"/> on <paramref name="server"/>.</summary>
    public static string For(NpgsqlConnectionStringBuilder server, string role) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"drover test role password: {role} at {server.Host}:{server.Port} for {server.Username}")));

    #endregion
}
