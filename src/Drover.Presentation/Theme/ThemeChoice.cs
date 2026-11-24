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

    private const string ThemeIdName = "themeId";

    private const string ModeName = "mode";

    private const string AccentName = "accent";

    private const string VersionName = "version";


    // A mode is one of these words. A number or a list of words is not one, whatever an enum would make of it.
    private static readonly Dictionary<string, ThemeMode> modes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["system"] = ThemeMode.System,
        ["dark"] = ThemeMode.Dark,
        ["light"] = ThemeMode.Light,
        ["black"] = ThemeMode.Black,
    };

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

        if (modes.TryGetValue(value.Trim(), out var mode))
        {
            return Default with { Mode = mode };
        }

        return ReadObject(value, logger) ?? Default;
    }

    // Written and read by hand, not by reflection: the browser build trims what a serializer would need to make a
    // record, and a choice it could not read would stop the app before its first screen.
    public string ToStored()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(ThemeIdName, ThemeId);
            writer.WriteString(ModeName, Mode.ToString().ToLowerInvariant());
            writer.WriteString(AccentName, Accent);
            writer.WriteNumber(VersionName, ChosenTheme);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    #endregion

    #region Private Methods

    private static ThemeChoice? ReadObject(string value, ILogger logger)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object
                || Text(root, ThemeIdName) is not { Length: > 0 } themeId
                || Text(root, ModeName) is not { } word
                || !modes.TryGetValue(word, out var mode)
                || !TryAccent(root, out var accent)
                || !TryVersion(root, out var version))
            {
                return null;
            }

            // Before a person could choose a theme, the object named the one theme there was. That was no choice.
            return version >= ChosenTheme
                ? new(themeId, mode, accent is { Length: > 0 } ? accent : Accents.ThemeId)
                : Default with { Mode = mode };
        }
        catch (JsonException exception)
        {
            // Not a choice this version wrote. The device is treated as one that has not chosen.
            logger.LogWarning(exception, "The stored theme choice is not a theme and a mode");
            return null;
        }
    }

    // A name is read as it is written, in the one casing, as the page that paints before the app reads it.
    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var found) && found.ValueKind is JsonValueKind.String ? found.GetString() : null;

    private static bool TryAccent(JsonElement root, out string? accent)
    {
        accent = null;
        if (!root.TryGetProperty(AccentName, out var found) || found.ValueKind is JsonValueKind.Null)
        {
            return true;
        }

        accent = found.ValueKind is JsonValueKind.String ? found.GetString() : null;
        return found.ValueKind is JsonValueKind.String;
    }

    private static bool TryVersion(JsonElement root, out int version)
    {
        version = 0;

        return !root.TryGetProperty(VersionName, out var found)
            || (found.ValueKind is JsonValueKind.Number && found.TryGetInt32(out version) && found.GetRawText() == $"{version}");
    }

    #endregion
}
