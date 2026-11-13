namespace Drover.Domain.Rules;

/// <summary>
/// The time one evaluation may spend looking for matches, across every rule, condition, and piece of text in it.
/// Once it is spent, whatever is still to be searched counts as matching: a rule that cannot finish applies.
/// </summary>
public sealed class RuleBudget(TimeSpan limit)
{
    #region Private Fields

    private readonly Stopwatch spent = new();

    #endregion

    #region Properties

    /// <summary>What one evaluation gets. A person's own patterns finish in far less; this stops the ones built not to.</summary>
    public static TimeSpan Default { get; } = TimeSpan.FromSeconds(1);

    public bool IsSpent => spent.Elapsed >= limit;

    #endregion

    #region Public Methods

    /// <summary>Counts the time until the returned value is disposed as time spent searching.</summary>
    public Running Search()
    {
        spent.Start();
        return new Running(spent);
    }

    #endregion

    #region Running

    /// <summary>One stretch of searching. Disposing it stops the count.</summary>
    public readonly struct Running(Stopwatch watch) : IDisposable
    {
        public void Dispose() => watch.Stop();
    }

    #endregion
}
