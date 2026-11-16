namespace Drover.Domain.Memory;

/// <summary>
/// A note's text as it is kept and shown: one line. Line breaks and other control characters become spaces and runs of
/// white space become one, so a note can never lay out text that passes for the end of the notes, another note, or
/// what a person said.
/// </summary>
public static partial class NoteText
{
    #region Public Methods

    public static string Flatten(string text)
    {
        var flat = new StringBuilder(text.Length);
        var space = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character) || char.GetUnicodeCategory(character) is UnicodeCategory.Format)
            {
                space = flat.Length > 0;
                continue;
            }

            if (space)
            {
                flat.Append(' ');
                space = false;
            }

            flat.Append(character);
        }

        return flat.ToString();
    }

    /// <summary>
    /// True when <paramref name="after"/> keeps no word of <paramref name="before"/>, ignoring case: nothing of the
    /// earlier text can have come through. Any word kept, however short, counts as keeping the earlier text.
    /// </summary>
    public static bool IsRewrite(string before, string after) => !Words(after).Overlaps(Words(before));

    #endregion

    #region Private Methods

    private static HashSet<string> Words(string text) =>
        [.. Word().Matches(text).Select(match => match.Value.ToLowerInvariant())];

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    #endregion
}
