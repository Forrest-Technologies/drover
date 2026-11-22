namespace Drover.Ui.Theme;

/// <summary>
/// Which theme a person chose and in which mode. It is kept on the device as one small JSON object. A value written
/// before themes existed was one word, the mode, and reads as Night Yard in that mode.
/// </summary>
public sealed record ThemeChoice(string ThemeId, ThemeMode Mode)
{
    #region Private Fields

    private static readonly JsonSerializerOptions stored = new(JsonSerializerDefaults.Web);

    #endregion

    #region Properties

    /// <summary>What a device that has never chosen gets: Night Yard, dark.</summary>
    public static ThemeChoice Default { get; } = new(BuiltInThemes.DefaultId, ThemeMode.Dark);

    #endregion

    #region Public Methods

    /// <summary>
    /// Reads what <see cref="ToStored"/> wrote, or the single word an older version wrote. A value that is neither
    /// is not an error: the device simply has not chosen, so it gets the default.
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

    public string ToStored() =>
        JsonSerializer.Serialize(new StoredChoice(ThemeId, Mode.ToString().ToLowerInvariant()), stored);

    #endregion

    #region Private Methods

    private static ThemeChoice? ReadObject(string value, ILogger logger)
    {
        try
        {
            var read = JsonSerializer.Deserialize<StoredChoice>(value, stored);

            return read is { ThemeId.Length: > 0 }
                && Enum.TryParse<ThemeMode>(read.Mode, ignoreCase: true, out var mode)
                && Enum.IsDefined(mode)
                ? new(read.ThemeId, mode)
                : null;
        }
        catch (JsonException exception)
        {
            // Not a choice this version wrote. The device is treated as one that has not chosen.
            logger.LogWarning(exception, "The stored theme choice is not a theme and a mode");
            return null;
        }
    }

    private sealed record StoredChoice(string ThemeId, string? Mode);

    #endregion
}
