namespace Drover.Domain.Runs;

/// <summary>
/// Tokens and cost for one model call. Carries the root run so cost rolls up the whole tree. Counts and cost are never
/// below zero: a negative figure a provider reports is stored as zero.
/// </summary>
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

    /// <summary>What the call cost at the catalog's prices. Null when the catalog did not know the price: never a made-up zero.</summary>
    public decimal? Cost { get; set; }

    public string Currency { get; set; } = "USD";

    public DateTimeOffset CreatedAt { get; set; }

    #endregion
}
