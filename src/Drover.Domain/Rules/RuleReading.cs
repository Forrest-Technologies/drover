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

    #endregion

    #region Properties

    public string Written => written;

    public RulePieceKind Kind => kind;

    public string? Argument => argument;

    /// <summary>The piece in its plain form, worked out once.</summary>
    public RuleText Text => text ??= new RuleText(written);

    #endregion
}

/// <summary>
/// One evaluation's reading of its subject: every piece of text in it, collected once and up to the cap, and what
/// each condition found in each piece, searched once however many rules ask the same thing.
/// </summary>
internal sealed class RuleReading
{
    #region Private Fields

    private readonly Dictionary<string, List<(int Start, int Length)>?[]> found = [];

    private readonly Dictionary<RuleCondition, string> keys = [];

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

    /// <summary>A search was cut short because the budget was spent.</summary>
    public bool RanOutOfTime { get; private set; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Where a text condition matches in a piece, as places in the piece as it was written. A search that cannot be
    /// made in time gives the whole piece.
    /// </summary>
    public List<(int Start, int Length)> Places(RuleCondition condition, int index)
    {
        var piece = Pieces[index];

        if (piece.Written.Length == 0)
        {
            return [];
        }

        // Conditions that ask the same thing share one search, whichever rules they belong to.
        if (!keys.TryGetValue(condition, out var key))
        {
            key = $"{condition.Kind}|{condition.Builtin}|{condition.Pattern}|{string.Join('\u0001', condition.Entries)}";
            keys[condition] = key;
        }

        if (!found.TryGetValue(key, out var places))
        {
            places = new List<(int Start, int Length)>?[Pieces.Count];
            found[key] = places;
        }

        if (places[index] is { } known)
        {
            return known;
        }

        if (Budget.IsSpent)
        {
            RanOutOfTime = true;
            return [(0, piece.Written.Length)];
        }

        var text = piece.Text;
        List<(int Start, int Length)> searched = [.. Search(condition, text.Plain).Select(place => text.InOriginal(place.Start, place.Length))];
        RanOutOfTime |= Budget.IsSpent;
        places[index] = searched;
        return searched;
    }

    #endregion

    #region Private Methods

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
    // the redaction walks in.
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
                foreach (var property in value.EnumerateObject())
                {
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

    private List<(int Start, int Length)> Search(RuleCondition condition, string text)
    {
        switch (condition.Kind)
        {
            case RuleConditionKind.TextMatches:
                return RulePatterns.Compile(condition.Pattern).Expression is { } expression ? RulePatterns.Find(expression, text, Budget) : [(0, text.Length)];
            case RuleConditionKind.TextHolds:
                return RulePatterns.Find(condition.Builtin ?? BuiltinPattern.Key, text, Budget);
            case RuleConditionKind.TextContains:
                List<(int, int)> places = [];

                foreach (var piece in condition.Entries.Select(entry => new RuleText(entry).Plain).Where(piece => piece.Length > 0))
                {
                    for (var at = text.IndexOf(piece, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(piece, at + piece.Length, StringComparison.OrdinalIgnoreCase))
                    {
                        places.Add((at, piece.Length));
                    }

                    if (Budget.IsSpent)
                    {
                        return [(0, text.Length)];
                    }
                }

                return places;
            default:
                return [];
        }
    }

    #endregion
}
