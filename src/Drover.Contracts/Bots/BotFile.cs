namespace Drover.Contracts.Bots;

/// <summary>
/// A bot as a file: one JSON document a person shares so someone else can copy the bot into their space. These are
/// all the fields a file has: a bot's sandbox, limits, schedules, memory, picture, rules, and grants are never part
/// of one. <see cref="Instructions"/> is a stranger's text until a person has read it. <see cref="SuggestedModel"/>
/// is advice: the server chooses a model the space has.
/// </summary>
public sealed record BotFile(int Version, string Name, string Instructions)
{
    #region Properties

    public const int CurrentVersion = 1;

    /// <summary>What other bots read to decide whether to hand this one a task.</summary>
    public string Description { get; init; } = string.Empty;

    public string? SuggestedModel { get; init; }

    #endregion
}

/// <summary>A bot file as text, and the name a saved one gets. Writing lives beside the format; reading is the server's alone.</summary>
public static class BotFiles
{
    #region Private Fields

    private static readonly JsonSerializerOptions written = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    #endregion

    #region Public Methods

    /// <summary>The bot file as the text of a file.</summary>
    public static string Write(BotFile file) => JsonSerializer.Serialize(file, written);

    /// <summary>The name a bot's file is saved under: its handle, then <c>.bot.json</c>.</summary>
    public static string NameFor(string handle) => $"{handle}.bot.json";

    #endregion
}

/// <summary>A bot file as its text, exactly as the person chose or pasted it. Sent to read the file, and sent again to import it.</summary>
public sealed record BotFileText(string File);

/// <summary>
/// The bot a file would add, for a person to read first. <see cref="Instructions"/> is the whole text the bot would
/// be given, shown in full and as plain text. When <see cref="HiddenCharacters"/> is above zero the text holds
/// characters that do not show on a screen, and each is written out where it stands, as <c>[U+200B]</c>, so nothing
/// in it is unseen. <see cref="Model"/> is the model the bot would think with and <see cref="ModelConnection"/> the
/// name of the connection it is on: the model the file suggests when that connection lists it, and the connection's
/// own otherwise. Both are null when the space has no model the bot could use, and then nothing can be imported.
/// </summary>
public sealed record BotImportPreview(string Name, string Description, string Instructions, int HiddenCharacters)
{
    #region Properties

    public string? Model { get; init; }

    public string? ModelConnection { get; init; }

    #endregion
}
