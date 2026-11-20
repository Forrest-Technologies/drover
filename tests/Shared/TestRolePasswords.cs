namespace Drover.Tests.Common;

/// <summary>
/// One password per role and PostgreSQL server, the same in every test assembly that runs against the same server.
/// Roles belong to the server, not to one database or process, so assemblies that each chose their own passwords
/// would reset one another's sign-ins when they run at once: the last bootstrap to run would make the others'
/// connections fail. Deriving from the server keeps every assembly on it in agreement, whatever order they boot in,
/// while a throwaway container (a connection of its own) still gets passwords nobody else has. Nothing is written
/// down: the value is computed from the server the caller already knows how to reach.
/// </summary>
public static class TestRolePasswords
{
    #region Public Methods

    /// <summary>The password this run's fixtures use for <paramref name="role"/> on <paramref name="server"/>.</summary>
    public static string For(NpgsqlConnectionStringBuilder server, string role) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"drover test role password: {role} at {server.Host}:{server.Port} for {server.Username}")));

    #endregion
}
