namespace Drover.Engine.Bots;

/// <summary>
/// Enumerations as the API spells them: lower-case words with underscores, the same spelling the database uses.
/// </summary>
public static class Words
{
    #region Public Methods

    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    /// <summary>The value a word names, or null when the word names none.</summary>
    public static TEnum? Read<TEnum>(string? word)
        where TEnum : struct, Enum
    {
        foreach (var value in Enum.GetValues<TEnum>())
        {
            if (string.Equals(Of(value), word, StringComparison.Ordinal))
            {
                return value;
            }
        }

        return null;
    }

    #endregion
}
