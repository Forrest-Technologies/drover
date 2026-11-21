namespace Drover.Presentation.Code;

/// <summary>
/// Rows of tab or comma separated cells, read as a table: at least two rows and two columns, every row with the same
/// number of cells. The first row is a header when it reads as names. A column whose cells are all numbers is
/// numeric, and is aligned to the end.
/// </summary>
public sealed record CodeTable(IReadOnlyList<string>? Header, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<bool> Numeric)
{
    #region Private Fields

    private const int MinimumRows = 2;

    private const int MinimumColumns = 2;

    #endregion

    #region Properties

    /// <summary>The number of columns, the same in every row.</summary>
    public int ColumnCount => Numeric.Count;

    /// <summary>What the table is called in its header and for someone who cannot see it: "Table, 12 rows".</summary>
    public string Label => $"Table, {Rows.Count.ToString("N0", CultureInfo.InvariantCulture)} {(Rows.Count == 1 ? "row" : "rows")}";

    #endregion

    #region Public Methods

    /// <summary>
    /// The text read as a table, tab separated first and then comma separated with quote rules, or nothing when it is
    /// not one. <paramref name="hasHeader"/> says the first row is a header whatever it holds, as a <c>csv</c> or
    /// <c>tsv</c> language does.
    /// </summary>
    public static CodeTable? Read(string text, bool hasHeader = false)
    {
        var rows = TabRows(text) ?? CommaRows(text);
        return rows is { Count: >= MinimumRows } read && read[0].Count >= MinimumColumns && read.All(row => row.Count == read[0].Count)
            ? Shaped(read, hasHeader)
            : null;
    }

    /// <summary>True when the cell is a number, as a table's numeric column holds: <c>12</c>, <c>-0.5</c>, <c>1,200</c>, <c>3e8</c>.</summary>
    public static bool IsNumber(string cell) =>
        cell.Trim() is { Length: > 0 } trimmed
        && decimal.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _);

    #endregion

    #region Reading

    private static List<string> LinesOf(string text)
    {
        List<string> lines = [.. text.Split('\n').Select(line => line.TrimEnd('\r'))];

        while (lines is [.., { Length: 0 }])
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    // Tab separated: every line holds a tab, and splits on it.
    private static List<IReadOnlyList<string>>? TabRows(string text)
    {
        var lines = LinesOf(text);
        return lines.Count > 0 && lines.All(line => line.Contains('\t')) ? [.. lines.Select(line => (IReadOnlyList<string>)line.Split('\t'))] : null;
    }

    // Comma separated, with quotes: a quoted cell holds commas, line breaks, and a doubled quote for a quote. Prose,
    // lines that end like sentences, is not a table, so a sentence with a comma never reads as one.
    private static List<IReadOnlyList<string>>? CommaRows(string text)
    {
        var lines = LinesOf(text);
        if (lines.Count == 0 || !lines.All(line => line.Contains(',') || line.Contains('"')) || ReadsAsProse(lines))
        {
            return null;
        }

        List<IReadOnlyList<string>> rows = [];
        List<string> cells = [];
        var cell = new StringBuilder();
        var quoted = false;
        var whole = string.Join('\n', lines);

        for (var index = 0; index < whole.Length; index++)
        {
            var character = whole[index];

            if (quoted)
            {
                if (character is not '"')
                {
                    cell.Append(character);
                }
                else if (index + 1 < whole.Length && whole[index + 1] is '"')
                {
                    cell.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (character)
            {
                case '"' when cell.Length == 0:
                    quoted = true;
                    break;
                case ',':
                    cells.Add(cell.ToString());
                    cell.Clear();
                    break;
                case '\n':
                    cells.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(cells);
                    cells = [];
                    break;
                default:
                    cell.Append(character);
                    break;
            }
        }

        if (quoted)
        {
            return null;
        }

        cells.Add(cell.ToString());
        rows.Add(cells);
        return rows;
    }

    private static bool ReadsAsProse(List<string> lines) =>
        lines.Count(line => line.TrimEnd() is [.., '.' or '!' or '?' or ':' or ';']) * 2 > lines.Count
        || lines.Any(line => line.Split(',').Any(part => part.Trim().Count(char.IsWhiteSpace) > 6));

    #endregion

    #region Shape

    // The first row is a header when the language says so, or when it reads as names: none of its cells is empty or a
    // number, so no header cell is a number while its column's other cells are.
    private static CodeTable Shaped(List<IReadOnlyList<string>> rows, bool hasHeader)
    {
        var header = hasHeader || rows[0].All(cell => cell.Trim().Length > 0 && !IsNumber(cell)) ? rows[0] : null;
        var body = header is null ? rows : rows[1..];
        bool[] numeric = [.. Enumerable.Range(0, rows[0].Count).Select(column => IsNumeric(body, column))];
        return new(header, body, numeric);
    }

    private static bool IsNumeric(List<IReadOnlyList<string>> rows, int column)
    {
        var filled = rows.Select(row => row[column]).Where(cell => cell.Trim().Length > 0).ToList();
        return filled.Count > 0 && filled.All(IsNumber);
    }

    #endregion
}
