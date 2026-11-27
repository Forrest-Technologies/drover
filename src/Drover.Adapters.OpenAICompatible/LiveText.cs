namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// The text a live session is started with and reported to, kept inside the provider's limits. The limits are in
/// tokens, which only the provider can count, so a count that errs high stands in for it.
/// </summary>
internal static class LiveText
{
    #region Private Fields

    /// <summary>The provider takes 8,192 tokens of earlier conversation in at most 128 messages.</summary>
    private const int MaxHistoryTokens = 7000;

    private const int MaxHistoryMessages = 128;

    private static readonly char[] sentenceEnds = ['.', '!', '?', '\n', ';', ':', ','];

    #endregion

    #region Public Methods

    /// <summary>
    /// What the model is told: who it speaks for, then when to hand work over. The first generation of the model is
    /// told to hand over in a wording of its own.
    /// </summary>
    public static string Instructions(RealtimeVoiceOptions options)
    {
        var (handOver, keep) = options.Model.Equals("gpt-live-1", StringComparison.OrdinalIgnoreCase)
            ? ("Generate <|SpawnThinking|>", "Do not generate <|SpawnThinking|>")
            : ("Delegate to the backend", "Do not delegate");

        return $"""
            {options.Instructions.Trim()}

            You speak for this bot. Its backend answers substantive questions and does the work.
            {keep} for greetings, thanks, simple questions about your name or role, or requests to repeat, slow down or stop.
            Answer name and role questions using your configured identity and description.
            {handOver} for all other questions, requests for opinions, or requests to do or change anything.
            Speak the backend's answers in the first person. Never claim work is done before the backend says so.
            Interruption policy: stop speaking when the person interrupts.
            """;
    }

    /// <summary>The earlier conversation as the session's input: its newest messages, as many as the limit takes, oldest first.</summary>
    public static IReadOnlyList<object> History(IReadOnlyList<RealtimeTurn> turns)
    {
        List<object> kept = [];
        var tokens = 0;

        for (var at = turns.Count - 1; at >= 0 && kept.Count < MaxHistoryMessages; at--)
        {
            tokens += Tokens(turns[at].Text);

            if (tokens > MaxHistoryTokens)
            {
                break;
            }

            kept.Add(new
            {
                type = "message",
                role = turns[at].FromBot ? "assistant" : "user",
                content = new[] { new { type = turns[at].FromBot ? "output_text" : "input_text", text = turns[at].Text } },
            });
        }

        kept.Reverse();
        return kept;
    }

    /// <summary>The text in pieces of at most <paramref name="maxTokens"/>, each ending where a sentence does when one ends in reach.</summary>
    public static IEnumerable<string> Pieces(string text, int maxTokens)
    {
        var rest = text.Trim();

        while (rest.Length > 0)
        {
            var length = Fits(rest, maxTokens);

            if (length < rest.Length && rest.LastIndexOfAny(sentenceEnds, length - 1) is var end && end > length / 2)
            {
                length = end + 1;
            }

            yield return rest[..length].Trim();
            rest = rest[length..].TrimStart();
        }
    }

    /// <summary>
    /// A count of tokens that is never under the provider's: a letter of the basic alphabet is a third of a token at
    /// least, and any other character can be two.
    /// </summary>
    public static int Tokens(string text)
    {
        var thirds = 0;

        foreach (var character in text)
        {
            thirds += char.IsAscii(character) ? 1 : 6;
        }

        return (thirds + 2) / 3;
    }

    #endregion

    #region Private Methods

    /// <summary>How many characters from the start fit in the count, never splitting a pair that makes one character.</summary>
    private static int Fits(string text, int maxTokens)
    {
        var thirds = 0;
        var length = 0;

        while (length < text.Length)
        {
            thirds += char.IsAscii(text[length]) ? 1 : 6;

            if (thirds > maxTokens * 3)
            {
                break;
            }

            length++;
        }

        if (length > 0 && length < text.Length && char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        return Math.Max(length, 1);
    }

    #endregion
}
