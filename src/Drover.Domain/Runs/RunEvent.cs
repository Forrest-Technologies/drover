namespace Drover.Domain.Runs;

/// <summary>
/// One event of a run tree, numbered per root run by <see cref="Seq"/>. The table is range-partitioned by month on
/// <see cref="CreatedAt"/>, so the key carries it too; <see cref="Seq"/> stays unique per root because it is given
/// out under a lock on the root run row.
/// </summary>
public sealed class RunEvent : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid SpaceId { get; set; }

    public Guid RootRunId { get; set; }

    public long Seq { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid RunId { get; set; }

    public Guid ThreadId { get; set; }

    public Guid AudienceUserId { get; set; }

    public required string Type { get; set; }

    public JsonElement Data { get; set; }

    #endregion
}
