namespace Drover.Domain.Runs;

/// <summary>Tokens and cost for one model call. Carries the root run so cost rolls up the whole tree.</summary>
public sealed class UsageRecord : ISpaceOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid RunId { get; set; }

    public Guid RootRunId { get; set; }

    public Guid BotId { get; set; }

    public Guid ModelConnectionId { get; set; }

    public required string Model { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long CachedTokens { get; set; }

    public decimal Cost { get; set; }

    public string Currency { get; set; } = "USD";

    public DateTimeOffset CreatedAt { get; set; }

    #endregion
}
