namespace Drover.Data.Tests.Isolation;

/// <summary>Expected privileges, declared independently of the installed database grants.</summary>
public sealed record AppGrantContract(bool AppendOnly, IReadOnlyList<string>? UpdateColumns = null, bool AllowDelete = true)
{
    #region Public Methods

    public static AppGrantContract For(string table) => table switch
    {
        "audit_log" => new(true),
        "user_profiles" => new(false, AllowDelete: false),
        "threads" => new(false, ["id", "space_id", "audience_user_id", "bot_id", "title", "origin",
            "last_activity_at", "archived_at"]),
        _ => new(false),
    };

    public bool AllowsTablePrivilege(string privilege) => privilege switch
    {
        "SELECT" or "INSERT" => true,
        "UPDATE" => !AppendOnly && UpdateColumns is null,
        "DELETE" => !AppendOnly && AllowDelete,
        _ => false,
    };

    public bool AllowsColumnUpdate(string column) =>
        !AppendOnly && (UpdateColumns is null || UpdateColumns.Contains(column));

    #endregion
}
