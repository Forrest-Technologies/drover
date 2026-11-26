namespace Drover.Contracts.Cards;

/// <summary>
/// Writes a card's fallback and the words of an answer. Both are pure: the fallback comes from the card's own
/// fields, and the words come from the stored card and the answer's indexes and typed text.
/// </summary>
public static class CardText
{
    #region Public Methods

    /// <summary>The card as plain text: its title, its text when there is one, and its options in order.</summary>
    public static string Plain(CardSpec card)
    {
        List<string> lines = [card.Title];

        if (card.Text is not null)
        {
            lines.Add(card.Text);
        }

        lines.AddRange(BodyLines(card));
        return string.Join('\n', lines);
    }

    /// <summary>The words a press sends, written from the stored card and never from the request's text.</summary>
    public static string Words(CardSpec card, CardAnswer answer) => card switch
    {
        RepliesSpec replies => ReplyWords(replies, answer),
        ChoicesSpec choices => ChoiceWords(choices, answer),
        FormSpec form => FormWords(form, answer),
        _ => string.Empty,
    };

    #endregion

    #region Private Methods

    private static List<string> BodyLines(CardSpec card) => card switch
    {
        RepliesSpec replies => Numbered(replies.Replies),
        ChoicesSpec choices => NumberedWithSomethingElse(choices),
        FormSpec form => [.. form.Fields.Select(field => field.Label)],
        ChecklistSpec checklist => [.. checklist.Items.Select(item => $"{item.Label} ({StateWord(item.State)})")],
        TableSpec table => TableLines(table),
        BoardSpec board => BoardLines(board),
        _ => [],
    };

    private static List<string> Numbered(IReadOnlyList<string> options)
    {
        List<string> numbered = [];

        for (var index = 0; index < options.Count; index++)
        {
            numbered.Add($"{index + 1}. {options[index]}");
        }

        return numbered;
    }

    private static List<string> NumberedWithSomethingElse(ChoicesSpec card)
    {
        var lines = Numbered(card.Choices);
        lines.Add($"{card.Choices.Count + 1}. Something else");
        return lines;
    }

    private static string ReplyWords(RepliesSpec card, CardAnswer answer)
    {
        var index = answer.Choices?.FirstOrDefault() ?? -1;
        return index >= 0 && index < card.Replies.Count ? card.Replies[index] : string.Empty;
    }

    private static string ChoiceWords(ChoicesSpec card, CardAnswer answer)
    {
        List<string> words =
        [
            ..(answer.Choices ?? [])
                .Where(index => index >= 0 && index < card.Choices.Count)
                .Select(index => card.Choices[index]),
        ];

        if (answer.Text is not null)
        {
            words.Add(answer.Text);
        }

        return string.Join(", ", words);
    }

    private static string FormWords(FormSpec card, CardAnswer answer) =>
        string.Join('\n', card.Fields.Select(field => $"{field.Label}: {answer.Values?.GetValueOrDefault(field.Name) ?? string.Empty}"));

    private static List<string> TableLines(TableSpec table)
    {
        List<string> lines = [string.Join(" | ", table.Columns)];

        foreach (var row in table.Rows)
        {
            lines.Add(string.Join(" | ", row));
        }

        return lines;
    }

    private static List<string> BoardLines(BoardSpec board)
    {
        List<string> lines = [];

        foreach (var figure in board.Figures)
        {
            lines.Add($"{figure.Value} {figure.Label}");
        }

        foreach (var state in board.States)
        {
            lines.Add($"{state.Label} ({StateWord(state.State)})");
        }

        foreach (var row in board.Rows)
        {
            lines.Add($"{row.Label}: {row.Value ?? string.Empty}");
        }

        return lines;
    }

    private static string StateWord(string state) => state switch
    {
        CardKinds.ItemToDo => "To do",
        CardKinds.ItemDoing => "Doing",
        CardKinds.ItemDone => "Done",
        CardKinds.ItemFailed => "Failed",
        CardKinds.BoardOk => "OK",
        CardKinds.BoardAttention => "Needs attention",
        CardKinds.BoardUnknown => "Not known",
        _ => state,
    };

    #endregion
}
