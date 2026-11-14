namespace Drover.Engine.Status;

/// <summary>How the database's migrations stand against the build that is running.</summary>
public enum MigrationVerdict
{
    /// <summary>The newest applied migration is the newest the build carries.</summary>
    UpToDate,

    /// <summary>No migration is applied: the one-off initialization has never run.</summary>
    NoneApplied,

    /// <summary>The build carries a newer migration than the database has: the initialization was skipped.</summary>
    Behind,

    /// <summary>The database has a newer migration than the build knows: an older build was started against it.</summary>
    Ahead,
}

/// <summary>The newest migration the database has and the newest this build carries. Neither is tenant data.</summary>
public sealed record MigrationState(string? Applied, string? Newest)
{
    #region Properties

    public MigrationVerdict Verdict => Applied switch
    {
        null => MigrationVerdict.NoneApplied,
        var applied when applied == Newest => MigrationVerdict.UpToDate,
        var applied when string.CompareOrdinal(applied, Newest) > 0 => MigrationVerdict.Ahead,
        _ => MigrationVerdict.Behind,
    };

    #endregion
}
