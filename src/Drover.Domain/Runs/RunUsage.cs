namespace Drover.Domain.Runs;

public sealed record RunUsage
{
    #region Properties

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long CachedTokens { get; init; }

    public decimal Cost { get; init; }

    #endregion
}
