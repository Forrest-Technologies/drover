namespace Drover.Domain.Rules;

/// <summary>
/// What one evaluation may spend: a length of time that covers everything it does (reading the text into its plain
/// form, every search, every rewrite), and a size it will look at. The clock starts when the budget is made. Once
/// the time is spent, or when there is more than the size, what is still to be decided cannot be checked, and a
/// rule that could not be checked applies.
/// </summary>
public sealed class RuleBudget(TimeSpan limit)
{
    #region Private Fields

    private readonly Stopwatch running = Stopwatch.StartNew();

    #endregion

    #region Properties

    /// <summary>What one evaluation gets. A person's own patterns finish in far less; this stops the ones built not to.</summary>
    public static TimeSpan Default { get; } = TimeSpan.FromSeconds(1);

    /// <summary>The most characters one evaluation looks at, over all its pieces of text. More is too much to check.</summary>
    public static int MaxCharacters => 500_000;

    /// <summary>The most pieces of text (strings, numbers, flags, names) one evaluation looks at.</summary>
    public static int MaxLeaves => 20_000;

    public bool IsSpent => running.Elapsed >= limit;

    #endregion
}
