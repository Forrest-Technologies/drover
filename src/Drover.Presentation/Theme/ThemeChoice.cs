namespace Drover.Ui.Theme;

/// <summary>
/// Which theme a person chose, in which mode, and with which accent. It is kept on the device as one small JSON
/// object. A value written before a person could choose a theme named no choice of theme: one word, the mode, or an
/// object without a version. Either reads as the default theme in the mode it names.
/// </summary>
public sealed record ThemeChoice(string ThemeId, ThemeMode Mode, string Accent = Accents.ThemeId)
{
    #region Private Fields

    // The version of the stored object from which the theme it names is one a person chose.
    private const int ChosenTheme = 2;

    private static readonly JsonSerializerOptions stored = new(JsonSerializerDefaults.Web);

    #endregion

    #region Properties

    /// <summary>What a device that has never chosen gets: the default theme, dark, in its own color.</summary>
    public static ThemeChoice Default { get; } = new(BuiltInThemes.DefaultId, ThemeMode.Dark);

    #endregion

    #region Public Methods

    /// <summary>
    /// Reads what <see cref="ToStored"/> wrote, or what an older version wrote. A value that is neither is not an
    /// error: the device simply has not chosen, so it gets the default.
    /// </summary>
    public static ThemeChoice Parse(string? value, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        if (Enum.TryParse<ThemeMode>(value, ignoreCase: true, out var mode) && Enum.IsDefined(mode))
        {
            return Default with { Mode = mode };
        }

        return ReadObject(value, logger) ?? Default;
    }

    public string ToStored() => JsonSerializer.Serialize(
        new StoredChoice(ThemeId, Mode.ToString().ToLowerInvariant(), Accent, ChosenTheme),
        stored);

    #endregion

    #region Private Methods

    private static ThemeChoice? ReadObject(string value, ILogger logger)
    {
        try
        {
            var read = JsonSerializer.Deserialize<StoredChoice>(value, stored);

            if (read is not { ThemeId.Length: > 0 }
                || !Enum.TryParse<ThemeMode>(read.Mode, ignoreCase: true, out var mode)
                || !Enum.IsDefined(mode))
            {
                return null;
            }

            // Before a person could choose a theme, the object named the one theme there was. That was no choice.
            return read.Version >= ChosenTheme
                ? new(read.ThemeId, mode, read.Accent is { Length: > 0 } accent ? accent : Accents.ThemeId)
                : Default with { Mode = mode };
        }
        catch (JsonException exception)
        {
            // Not a choice this version wrote. The device is treated as one that has not chosen.
            logger.LogWarning(exception, "The stored theme choice is not a theme and a mode");
            return null;
        }
    }

    private sealed record StoredChoice(string ThemeId, string? Mode, string? Accent = null, int Version = 0);

    #endregion
}
