namespace Drover.Domain.Memory;

/// <summary>
/// A note's text as it is kept and shown: one line. Line breaks and other control characters become spaces and runs of
/// white space become one, so a note can never lay out text that passes for the end of the notes, another note, or
/// what a person said.
/// </summary>
public static class NoteText
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

    #endregion
}
