namespace Drover.Domain.Rules;

/// <summary>What a piece of text in a subject is, which decides whether a rule may rewrite it.</summary>
internal enum RulePieceKind
{
    /// <summary>A reply or a message.</summary>
    Said,

    /// <summary>A string in a tool's input or result.</summary>
    Value,

    /// <summary>A number, a flag, or null: examined as written and never rewritten.</summary>
    Other,

    /// <summary>The name of a property: examined and never rewritten.</summary>
    Name,
}

/// <summary>One piece of text an evaluation looks at, and the top-level input it is under, if any.</summary>
internal sealed class RulePiece(string written, RulePieceKind kind, string? argument)
{
    #region Private Fields

    private RuleText? text;

    private bool read;

    #endregion

    #region Properties

    public string Written => written;

    public RulePieceKind Kind => kind;

    public string? Argument => argument;

    #endregion

    #region Public Methods

    /// <summary>The piece in its plain form, worked out and paid for once, or null when the budget could not pay.</summary>
    public RuleText? Text(RuleBudget budget)
    {
        if (!read)
        {
            text = RuleText.Read(written, budget);
            read = text is not null;
        }

        return text;
    }

    #endregion
}

/// <summary>
/// What one text condition found in one reading: where it matches in each piece it looks at, searched once however
/// many rules ask the same thing.
/// </summary>
internal sealed class RuleSearch(RuleCondition condition, int pieces)
{
    #region Properties

    public RuleCondition Condition => condition;

    /// <summary>Where the condition matches in each piece, by the piece's place in the reading. Null where it does not look.</summary>
    public List<(int Start, int Length)>?[] Places { get; } = new List<(int Start, int Length)>?[pieces];

    public bool IsDone { get; set; }

    /// <summary>The condition matches somewhere it looks.</summary>
    public bool Found { get; set; }

    /// <summary>The search could not be finished: the budget was spent.</summary>
    public bool CouldNotCheck { get; set; }

    /// <summary>It matches in the name of an input, which a rule cannot rewrite.</summary>
    public bool InAName { get; set; }

    /// <summary>It matches in a number, a flag, or null, which a rule cannot rewrite.</summary>
    public bool InWhatIsNotText { get; set; }

    #endregion
}

/// <summary>
/// One evaluation's reading of its subject: every piece of text in it, collected once and up to the cap, and what
/// each text condition found, searched once. Everything here that looks at text pays the evaluation's budget.
/// </summary>
internal sealed class RuleReading
{
    #region Private Fields

    private readonly Dictionary<string, RuleSearch> searches = [];

    private int characters;

    #endregion

    #region Constructors

    public RuleReading(RuleSubject subject, RuleBudget budget)
    {
        Subject = subject;
        Budget = budget;

        if (subject.Text is { } text)
        {
            Add(text, RulePieceKind.Said, null);
        }

        if (subject.Json is { } json)
        {
            Collect(json, null, atTop: true);
        }
    }

    #endregion

    #region Properties

    public RuleSubject Subject { get; }

    public RuleBudget Budget { get; }

    public List<RulePiece> Pieces { get; } = [];

    /// <summary>There is more text, or there are more pieces, than an evaluation looks at.</summary>
    public bool IsTooMuch { get; private set; }

    /// <summary>Two inputs of one object share a name, so the object cannot be written out again as it was.</summary>
    public bool HasSharedNames { get; private set; }

    #endregion

    #region Public Methods

    /// <summary>What a text condition finds in this reading. Conditions that ask the same thing share one search.</summary>
    public RuleSearch Search(RuleCondition condition)
    {
        var key = $"{condition.Kind}|{condition.Builtin}|{condition.Argument}|{condition.Pattern}|{string.Join('\u0001', condition.Entries)}";

        if (!searches.TryGetValue(key, out var search))
        {
            search = new RuleSearch(condition, Pieces.Count);
            searches[key] = search;
        }

        if (!search.IsDone)
        {
            Run(search);
        }

        return search;
    }

    /// <summary>A condition with inputs named looks only at what is under those inputs. Without, at everything.</summary>
    public static bool InScope(RuleCondition condition, RulePiece piece) =>
        condition.Argument is null || (piece.Argument is { } under && Names(condition.Argument).Contains(under, StringComparer.OrdinalIgnoreCase));

    /// <summary>The inputs a condition names: one, or several parted by commas.</summary>
    public static string[] Names(string? argument) =>
        argument?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];

    #endregion

    #region Private Methods

    // One pass over the pieces the condition looks at. Looking at a piece is a unit of work; reading and searching
    // it is paid by the reader and the scanner.
    private void Run(RuleSearch search)
    {
        search.IsDone = true;
        var condition = search.Condition;
        var entries = condition.Kind is RuleConditionKind.TextContains ? ReadEntries(condition) : [];

        if (entries is null)
        {
            search.CouldNotCheck = true;
            return;
        }

        var regex = condition.Kind is RuleConditionKind.TextMatches ? RulePatterns.Compile(condition.Pattern).Expression : null;

        for (var index = 0; index < Pieces.Count; index++)
        {
            var piece = Pieces[index];

            if (!InScope(condition, piece) || piece.Written.Length == 0)
            {
                continue;
            }

            if (!Budget.TrySpend(1) || piece.Text(Budget) is not { } text || Find(condition, regex, entries, text) is not { } places)
            {
                search.CouldNotCheck = true;
                return;
            }

            if (places.Count == 0)
            {
                continue;
            }

            search.Places[index] = [.. places.Select(place => text.InOriginal(place.Start, place.Length))];
            search.Found = true;
            search.InAName |= piece.Kind is RulePieceKind.Name;
            search.InWhatIsNotText |= piece.Kind is RulePieceKind.Other;
        }
    }

    // Where a condition matches in one text, in its plain form, or null when the budget could not pay for looking.
    private List<(int Start, int Length)>? Find(RuleCondition condition, Regex? regex, List<string> entries, RuleText text)
    {
        switch (condition.Kind)
        {
            case RuleConditionKind.TextMatches:
                // A pattern that cannot be read is treated as matching everything: a rule fails closed.
                return regex is null ? [(0, text.Plain.Length)] : RulePatterns.Find(regex, text.Plain, Budget);
            case RuleConditionKind.TextHolds:
                return RulePatterns.Find(condition.Builtin ?? BuiltinPattern.Key, text.Plain, Budget);
            default:
                List<(int, int)> places = [];

                foreach (var entry in entries)
                {
                    // Each thing looked for is one reading of the text.
                    if (!Budget.TrySpend(text.Plain.Length))
                    {
                        return null;
                    }

                    for (var at = text.Plain.IndexOf(entry, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.Plain.IndexOf(entry, at + entry.Length, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!Budget.TryKeep())
                        {
                            return null;
                        }

                        places.Add((at, entry.Length));
                    }
                }

                return places;
        }
    }

    // What a "contains" condition looks for, in its plain form and each thing once, or null when the budget could
    // not pay for reading it. A rule is refused at save when two of its entries read the same; one saved before
    // that was checked is still searched for each thing once.
    private List<string>? ReadEntries(RuleCondition condition)
    {
        List<string> entries = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in condition.Entries)
        {
            if (RuleText.Read(entry, Budget) is not { } read)
            {
                return null;
            }

            if (read.Plain.Length > 0 && seen.Add(read.Plain))
            {
                entries.Add(read.Plain);
            }
        }

        return entries;
    }

    private void Add(string written, RulePieceKind kind, string? argument)
    {
        characters += written.Length;

        if (IsTooMuch || characters > RuleBudget.MaxCharacters || Pieces.Count >= RuleBudget.MaxLeaves)
        {
            IsTooMuch = true;
            return;
        }

        Pieces.Add(new RulePiece(written, kind, argument));
    }

    // An input is a property of the top object; everything beneath it belongs to it. The order here is the order
    // the redaction walks in. The cap bounds the walk, so it spends nothing.
    private void Collect(JsonElement value, string? argument, bool atTop)
    {
        if (IsTooMuch)
        {
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                Add(value.GetString() ?? string.Empty, RulePieceKind.Value, argument);
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    Collect(item, argument, atTop: false);
                }

                break;
            case JsonValueKind.Object:
                HashSet<string> names = [];

                foreach (var property in value.EnumerateObject())
                {
                    HasSharedNames |= !names.Add(property.Name);
                    Add(property.Name, RulePieceKind.Name, argument);
                    Collect(property.Value, atTop ? property.Name : argument, atTop: false);
                }

                break;
            default:
                // A number can be a card number as well as a string can, and a flag is a word.
                Add(value.GetRawText(), RulePieceKind.Other, argument);
                break;
        }
    }

    #endregion
}
