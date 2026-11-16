namespace Drover.Ui.Starters;

/// <summary>What happened to one line between two versions of a text.</summary>
public enum DiffKind
{
    Same,
    Removed,
    Added,
}

/// <summary>One line of a comparison.</summary>
public sealed record DiffLine(DiffKind Kind, string Text);

/// <summary>
/// Two versions of a text compared line by line, for instructions and other long values in a bot's history.
/// It keeps the longest run of lines the two share and marks the rest as removed or added, removed first.
/// </summary>
public static class LineDiff
{
    #region Public Methods

    public static IReadOnlyList<DiffLine> Between(string? before, string? after)
    {
        var old = Lines(before);
        var now = Lines(after);
        var shared = SharedLengths(old, now);

        List<DiffLine> lines = [];
        var (i, j) = (0, 0);
        while (i < old.Length || j < now.Length)
        {
            if (i < old.Length && j < now.Length && old[i] == now[j])
            {
                lines.Add(new(DiffKind.Same, old[i]));
                (i, j) = (i + 1, j + 1);
            }
            else if (i < old.Length && (j == now.Length || shared[i + 1, j] >= shared[i, j + 1]))
            {
                lines.Add(new(DiffKind.Removed, old[i++]));
            }
            else
            {
                lines.Add(new(DiffKind.Added, now[j++]));
            }
        }

        return lines;
    }

    /// <summary>The change in a sentence, for whoever cannot see the marks: "2 lines added, 1 removed".</summary>
    public static string Summary(IReadOnlyCollection<DiffLine> lines)
    {
        var added = lines.Count(line => line.Kind is DiffKind.Added);
        var removed = lines.Count(line => line.Kind is DiffKind.Removed);
        return (added, removed) switch
        {
            (0, 0) => "No lines changed",
            (_, 0) => $"{Words.Count(added, "line")} added",
            (0, _) => $"{Words.Count(removed, "line")} removed",
            _ => $"{Words.Count(added, "line")} added, {removed} removed",
        };
    }

    #endregion

    #region Private Methods

    // A text that ends with a line ending has no empty line after that ending. Blank lines before it are lines.
    private static string[] Lines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var lines = text.ReplaceLineEndings("\n");
        return (lines.EndsWith('\n') ? lines[..^1] : lines).Split('\n');
    }

    // shared[i, j] is how many lines old[i..] and now[j..] have in common, in order.
    private static int[,] SharedLengths(string[] old, string[] now)
    {
        var shared = new int[old.Length + 1, now.Length + 1];
        for (var i = old.Length - 1; i >= 0; i--)
        {
            for (var j = now.Length - 1; j >= 0; j--)
            {
                shared[i, j] = old[i] == now[j] ? shared[i + 1, j + 1] + 1 : Math.Max(shared[i + 1, j], shared[i, j + 1]);
            }
        }

        return shared;
    }

    #endregion
}
