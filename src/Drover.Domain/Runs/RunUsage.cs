namespace Drover.Domain.Runs;

public sealed record RunUsage
{
    #region Properties

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long CachedTokens { get; init; }

    public decimal Cost { get; init; }

    #endregion

    #region Public Methods

    public RunUsage Plus(RunUsage other) =>
        new()
        {
            InputTokens = InputTokens + other.InputTokens,
            OutputTokens = OutputTokens + other.OutputTokens,
            CachedTokens = CachedTokens + other.CachedTokens,
            Cost = Cost + other.Cost,
        };

    #endregion
}
