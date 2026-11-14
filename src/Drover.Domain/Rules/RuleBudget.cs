namespace Drover.Domain.Rules;

/// <summary>
/// What one evaluation may spend. Work is counted, not timed: every loop that looks at text spends units from the
/// one budget of its evaluation (a unit is a character examined, a piece or an element looked at, an entry
/// compared, or a match kept), so the same input gets the same answer on a busy server and an idle one. There is
/// no way to search text without a budget. When the budget is spent, what is still to be decided could not be
/// checked, and a rule that could not be checked applies. One cost cannot be counted, and nothing here bounds it:
/// what a written pattern does inside the expression engine during one match. The clock is read between matches
/// only, and the engine's own match timeout is not looked at while it builds states. What keeps that cost small is
/// that a written pattern is tried on sample text before it is ever run (<see cref="RulePatterns"/>), and that a
/// shared server does not run written patterns at all.
/// </summary>
public sealed class RuleBudget(long work, TimeSpan backstop)
{
    #region Private Fields

    private readonly Stopwatch running = Stopwatch.StartNew();

    private bool spent;

    #endregion

    #region Constructors

    public RuleBudget(long work)
        : this(work, Backstop)
    {
    }

    #endregion

    #region Properties

    /// <summary>The work one evaluation gets: about a hundred readings of the most text it will look at.</summary>
    public static long MaxWork => 50_000_000;

    /// <summary>The longest one evaluation may run, whatever its work comes to.</summary>
    public static TimeSpan Backstop { get; } = TimeSpan.FromSeconds(2);

    /// <summary>The most characters one evaluation looks at, over all its pieces of text. More is too much to check.</summary>
    public static int MaxCharacters => 500_000;

    /// <summary>The most pieces of text (strings, numbers, flags, names) one evaluation looks at.</summary>
    public static int MaxLeaves => 20_000;

    /// <summary>The most matches one evaluation keeps, over all its searches. More could not be checked.</summary>
    public static int MaxMatches => 100_000;

    /// <summary>The matches kept so far.</summary>
    public int Kept { get; private set; }

    /// <summary>The units of work spent so far.</summary>
    public long Spent { get; private set; }

    /// <summary>Whether work was refused. Once it was, the evaluation could not check everything.</summary>
    public bool IsSpent => spent;

    #endregion

    #region Public Methods

    /// <summary>
    /// Takes units of work before the work is done. False when the budget does not have them, or the backstop has
    /// run out: the caller then does not do the work and treats its subject as not checked.
    /// </summary>
    public bool TrySpend(long units)
    {
        if (spent || units > work - Spent || running.Elapsed >= backstop)
        {
            spent = true;
            return false;
        }

        Spent += units;
        return true;
    }

    /// <summary>
    /// Takes room for one more match before it is kept: a unit of work, and one of the matches an evaluation may
    /// hold. False when either has run out: the caller keeps nothing more and treats its subject as not checked.
    /// </summary>
    public bool TryKeep()
    {
        if (Kept >= MaxMatches)
        {
            spent = true;
            return false;
        }

        if (!TrySpend(1))
        {
            return false;
        }

        Kept++;
        return true;
    }

    #endregion
}
