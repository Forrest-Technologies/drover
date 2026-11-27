namespace Drover.Domain.Runs;

public sealed record RunUsage
{
    #region Properties

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long CachedTokens { get; init; }

    public decimal Cost { get; init; }

    /// <summary>The rule checks a decision model made for the run, counted against <c>MaxJudgedChecksPerRun</c>.</summary>
    public int JudgedChecks { get; init; }

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
