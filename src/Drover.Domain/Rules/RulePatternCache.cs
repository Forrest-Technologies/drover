namespace Drover.Domain.Rules;

/// <summary>
/// The written patterns a server keeps ready, bounded by what they hold. An expression grows as it meets new text
/// (it builds its states as it goes), so each one is charged what it allocated when it was tried and each time it
/// is used, and the one used longest ago goes first when the whole is over its bound. A pattern that was refused
/// keeps its refusal here, in words, and never its expression.
/// </summary>
internal sealed class RulePatternCache(long mostBytes, int mostPatterns)
{
    #region Private Fields

    private readonly Lock gate = new();

    private readonly Dictionary<string, LinkedListNode<Entry>> byPattern = [];

    private readonly LinkedList<Entry> byUse = new();

    private long bytes;

    #endregion

    #region Properties

    /// <summary>How many patterns are kept, and what they are counted as holding.</summary>
    public (int Patterns, long Bytes) Held
    {
        get
        {
            lock (gate)
            {
                return (byPattern.Count, bytes);
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

    /// <summary>Keeps an expression with what it allocated so far, or a refusal with no expression.</summary>
    public void Keep(string pattern, Regex? expression, string? problem, long allocated)
    {
        lock (gate)
        {
            Forget(pattern);
            var entry = new Entry(pattern, expression, problem) { Bytes = (pattern.Length + (problem?.Length ?? 0)) * sizeof(char) + Math.Max(allocated, 0) };
            byPattern[pattern] = byUse.AddFirst(entry);
            bytes += entry.Bytes;
            Trim();
        }
    }

    /// <summary>Adds what a use of an expression allocated to what it is counted as holding.</summary>
    public void Charge(Regex expression, long allocated)
    {
        if (allocated <= 0)
        {
            return;
        }

        lock (gate)
        {
            if (byPattern.TryGetValue(expression.ToString(), out var node) && ReferenceEquals(node.Value.Expression, expression))
            {
                node.Value.Bytes += allocated;
                bytes += allocated;
                Trim();
            }
        }
    }

    #endregion

    #region Private Methods

    private void Forget(string pattern)
    {
        if (byPattern.Remove(pattern, out var node))
        {
            byUse.Remove(node);
            bytes -= node.Value.Bytes;
        }
    }

    // The pattern used longest ago goes first. One that alone holds more than the bound goes at once.
    private void Trim()
    {
        while (byUse.Last is { } last && (bytes > mostBytes || byPattern.Count > mostPatterns))
        {
            Forget(last.Value.Pattern);
        }
    }

    #endregion

    #region Nested Types

    private sealed class Entry(string pattern, Regex? expression, string? problem)
    {
        #region Properties

        public string Pattern => pattern;

        public Regex? Expression => expression;

        public string? Problem => problem;

        public long Bytes { get; set; }

        #endregion
    }

    #endregion
}
