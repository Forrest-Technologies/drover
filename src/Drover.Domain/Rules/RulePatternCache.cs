namespace Drover.Domain.Rules;

/// <summary>
/// The written patterns a server keeps ready, bounded in number: the one used longest ago goes first. A pattern
/// that was refused keeps its refusal here, in words, and never an expression.
/// </summary>
internal sealed class RulePatternCache(int mostPatterns)
{
    #region Private Fields

    private readonly Lock gate = new();

    private readonly Dictionary<string, LinkedListNode<Entry>> byPattern = [];

    private readonly LinkedList<Entry> byUse = new();

    #endregion

    #region Properties

    public int Count
    {
        get
        {
            lock (gate)
            {
                return byPattern.Count;
            }
        }
    }

    #endregion

    #region Public Methods

    public bool TryGet(string pattern, out (Regex? Expression, string? Problem) known)
    {
        lock (gate)
        {
            if (!byPattern.TryGetValue(pattern, out var node))
            {
                known = default;
                return false;
            }

            byUse.Remove(node);
            byUse.AddFirst(node);
            known = (node.Value.Expression, node.Value.Problem);
            return true;
        }
    }

    public void Keep(string pattern, Regex? expression, string? problem)
    {
        lock (gate)
        {
            if (byPattern.Remove(pattern, out var known))
            {
                byUse.Remove(known);
            }

            byPattern[pattern] = byUse.AddFirst(new Entry(pattern, expression, problem));

            while (byPattern.Count > mostPatterns && byUse.Last is { } last)
            {
                byUse.RemoveLast();
                byPattern.Remove(last.Value.Pattern);
            }
        }
    }

    #endregion

    #region Nested Types

    private sealed record Entry(string Pattern, Regex? Expression, string? Problem);

    #endregion
}
