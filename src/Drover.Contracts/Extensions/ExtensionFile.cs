namespace Drover.Contracts.Extensions;

/// <summary>
/// An extension as the text of a file, and the id a file gives itself. Writing lives beside the format so the server
/// that exports a bot and the editor that shows a file as text write the same thing. Reading is the server's alone.
/// </summary>
public static class ExtensionFile
{
    #region Private Fields

    public const int MinIdLength = 3;

    public const int MaxIdLength = 100;

    private static readonly JsonSerializerOptions written = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    #endregion

    #region Public Methods

    /// <summary>The package as the text of a file.</summary>
    public static string Write(ExtensionPackage package) => JsonSerializer.Serialize(package, written);

    /// <summary>
    /// An id made from a name: its letters and digits in lower case, everything else a hyphen, never two hyphens
    /// together or one at an end. A name with too little in it to make an id gets the word "extension" in front, so
    /// the result is always an id the format accepts.
    /// </summary>
    public static string IdFrom(string name)
    {
        var id = new StringBuilder(name.Length);

        foreach (var letter in name.ToLowerInvariant())
        {
            if (letter is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                id.Append(letter);
            }
            else if (id.Length > 0 && id[^1] is not '-')
            {
                id.Append('-');
            }
        }

        var made = id.ToString().Trim('-');
        made = made.Length < MinIdLength ? $"extension-{made}".TrimEnd('-') : made;
        return made.Length > MaxIdLength ? made[..MaxIdLength].TrimEnd('-') : made;
    }

    #endregion
}
