namespace Drover.Server.Health;

/// <summary>
/// What to tell an operator when the database's migrations do not match the build. One place, so readiness, the
/// start-up log and the status page say the same thing. Nothing here names a connection, a secret or a tenant.
/// </summary>
public static class MigrationAdvice
{
    #region Public Methods

    /// <summary>The description readiness carries. Null when the database is up to date.</summary>
    public static string? Readiness(MigrationState state) =>
        state.Verdict switch
        {
            MigrationVerdict.UpToDate => null,
            MigrationVerdict.Ahead => AheadReadiness,
            _ => BehindReadiness,
        };

    #endregion

    #region Constants

    /// <summary>The fix for a database that is behind the build, or has never been initialized.</summary>
    public const string Initialize =
        "Run the database initialization: docker compose up does it, and elsewhere a one-off run of the image with --initialize-database.";

    /// <summary>
    /// What to do when the database is ahead of the build, as after a rollback. It is not ready for the same reason
    /// the status page shows a problem: the older build has not been written for a schema it does not know.
    /// Running the initialization cannot help, because migrations only go forward.
    /// </summary>
    public const string Ahead =
        "Deploy the build that carries it, or a newer one. --initialize-database cannot undo a migration.";

    private const string BehindReadiness =
        "The database's migrations are behind this build. Run the --initialize-database step (a one-off run of this image with that command); this server is ready once it has finished.";

    private const string AheadReadiness =
        "The database's migrations are newer than this build knows. Deploy the build that carries them, or a newer one; --initialize-database cannot undo a migration.";

    #endregion
}
