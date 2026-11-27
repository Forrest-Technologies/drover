namespace Drover.Engine.Bots;

/// <summary>A bot file as it was read: the file, or the sentence that says why it is not one.</summary>
public sealed record BotFileReading(BotFile? File, string? Problem);

/// <summary>
/// Reads a bot file. The file is a stranger's text, so the reading is strict: a field this server does not know
/// refuses the whole file, because skipping what is not understood would add something other than what the file
/// says. Nothing here contacts anything.
/// </summary>
public static class BotFileReader
{
    #region Private Fields

    /// <summary>The most bytes of a bot file. A bot's instructions are the largest thing in one.</summary>
    public const int MaxFileBytes = 256 * 1024;

    private static readonly JsonSerializerOptions strict = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 8,
    };

    #endregion

    #region Public Methods

    public static BotFileReading Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new(null, "Choose a bot file, or paste its text.");
        }

        if (Encoding.UTF8.GetByteCount(text) > MaxFileBytes)
        {
            return new(null, $"This file is larger than a bot file can be ({MaxFileBytes / 1024} KB).");
        }

        try
        {
            if (IsExtension(text))
            {
                return new(null, "This is an extension file, not a bot file. Import it on the Extensions screen.");
            }

            if (JsonSerializer.Deserialize<BotFile>(text, strict) is not { } file)
            {
                return new(null, NotABot(null));
            }

            return FindProblem(file) is { } problem ? new(null, problem) : new(file, null);
        }
        catch (JsonException unreadable)
        {
            return new(null, NotABot(unreadable.Path));
        }
    }

    /// <summary>
    /// The text with every character that does not show on a screen written out where it stands, as
    /// <c>[U+200B]</c>, and how many there were. Line breaks and tabs show, so they stay.
    /// </summary>
    public static (string Text, int Hidden) ShowHidden(string text)
    {
        var shown = new StringBuilder(text.Length);
        var hidden = 0;
        var index = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsHidden(rune))
            {
                hidden++;
                shown.Append(CultureInfo.InvariantCulture, $"[U+{rune.Value:X4}]");
            }
            else
            {
                shown.Append(text.AsSpan(index, rune.Utf16SequenceLength));
            }

            index += rune.Utf16SequenceLength;
        }

        return (shown.ToString(), hidden);
    }

    #endregion

    #region Private Methods

    private static string NotABot(string? path) =>
        string.IsNullOrEmpty(path) || path is "$"
            ? "This is not a bot file Drover can read."
            : $"This is not a bot file Drover can read. The trouble is at {path}.";

    /// <summary>An extension file has contents, and a bot file has no such field: told apart so the refusal can say where the file goes.</summary>
    private static bool IsExtension(string text)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64 });
        return document.RootElement.ValueKind is JsonValueKind.Object && document.RootElement.TryGetProperty("contents", out _);
    }

    private static string? FindProblem(BotFile bot) =>
        bot.Version != BotFile.CurrentVersion ? $"This bot file is written in version {bot.Version} of the format. This server reads version {BotFile.CurrentVersion}."
        : string.IsNullOrWhiteSpace(bot.Name) || bot.Name.Trim().Length > BotService.MaxNameLength ? $"The bot in this file needs a name of up to {BotService.MaxNameLength} characters."
        : bot.Description.Length > BotService.MaxDescriptionLength ? $"What the bot in this file is for must be under {BotService.MaxDescriptionLength} characters."
        : bot.Instructions.Length > BotService.MaxInstructionsLength ? $"The instructions of the bot in this file must be under {BotService.MaxInstructionsLength} characters."
        : bot.SuggestedModel is { Length: > BotService.MaxModelLength } ? $"The suggested model's name must be under {BotService.MaxModelLength} characters."
        : null;

    private static bool IsHidden(Rune rune) =>
        Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.Control => rune.Value is not ('\n' or '\r' or '\t'),
            UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => true,
            UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned => true,
            _ => false,
        };

    #endregion
}
