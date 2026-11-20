namespace Drover.Contracts.Bots;

/// <summary>
/// How a bot's handle is made from its name, the same on the server and in every client, so a handle that was made
/// from a name can be told from one a person chose.
/// </summary>
public static class BotHandles
{
    #region Public Methods

    /// <summary>A name as a handle: lower-case letters and digits, with one hyphen for each run of anything else.</summary>
    public static string Slug(string name)
    {
        var slug = new StringBuilder(name.Length);
        var separated = false;

        foreach (var letter in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(letter))
            {
                if (separated && slug.Length > 0)
                {
                    slug.Append('-');
                }

                separated = false;
                slug.Append(letter);
            }
            else
            {
                separated = true;
            }
        }

        return slug.ToString();
    }

    /// <summary>
    /// The handle a name makes before the server adds a number to keep it unique: its slug, cut short to leave room
    /// for the number, or "bot" for a name with no letter or digit.
    /// </summary>
    public static string Stem(string name)
    {
        var slug = Slug(name);
        return slug.Length == 0 ? DefaultStem : slug[..Math.Min(slug.Length, MaxStemLength)].Trim('-');
    }

    /// <summary>
    /// Whether the handle is one the name made: its stem, or its stem with the number the server adds when the stem is
    /// taken ("new-bot-2"). A handle a person typed that is neither never follows the name. A handle that is not there
    /// yet follows it, because nothing has been chosen.
    /// </summary>
    public static bool Follows(string handle, string name)
    {
        if (handle.Length == 0)
        {
            return true;
        }

        var stem = Stem(name);

        if (handle == stem)
        {
            return true;
        }

        return handle.StartsWith($"{stem}-", StringComparison.Ordinal)
            && int.TryParse(handle.AsSpan(stem.Length + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
            && number >= 2;
    }

    #endregion

    #region Limits

    /// <summary>The longest handle.</summary>
    public const int MaxLength = 100;

    private const int MaxStemLength = MaxLength - 8;

    private const string DefaultStem = "bot";

    #endregion
}
