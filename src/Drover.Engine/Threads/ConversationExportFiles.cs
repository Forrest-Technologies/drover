namespace Drover.Engine.Threads;

/// <summary>
/// An exported conversation as a file a person keeps: Markdown to read, or the versioned JSON contract to process.
/// Names a person or a bot chose are written so they cannot change the document's shape; the words of the
/// conversation are written as they were said, because they are Markdown already.
/// </summary>
public static class ConversationExportFiles
{
    #region Private Fields

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private const string MarkdownSigns = "\\`*_[]<>#|~!";

    private const long Kilobyte = 1024;

    private const long Megabyte = 1024 * Kilobyte;

    #endregion

    #region Public Methods

    public static byte[] ToJson(this ConversationExport export) => JsonSerializer.SerializeToUtf8Bytes(export, json);

    public static byte[] ToMarkdown(this ConversationExport export)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# Conversation with {Inline(export.BotName)}\n\n");

        if (export.Title is { Length: > 0 } title)
        {
            text.Append(CultureInfo.InvariantCulture, $"{Inline(title)}\n\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"Saved {Time(export.ExportedAt)}. Export format version {export.Version}.\n");

        foreach (var message in export.Messages)
        {
            AppendMessage(text, message, export.BotName);
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    /// <summary>A name for the saved file: the bot's name and the day, with the format's extension.</summary>
    public static string FileName(this ConversationExport export, string format)
    {
        var name = new string([.. export.BotName.Where(letter => char.IsLetterOrDigit(letter) || letter is ' ' or '-' or '_')]).Trim();
        var extension = format is ExportFormats.Json ? "json" : "md";
        return $"{(name.Length > 0 ? name : "Drover")} conversation {export.ExportedAt.UtcDateTime:yyyy-MM-dd}.{extension}";
    }

    #endregion

    #region Private Methods

    private static void AppendMessage(StringBuilder text, ExportedMessage message, string botName)
    {
        var speaker = message.Role is "user" ? "Person" : Inline(botName);
        text.Append(CultureInfo.InvariantCulture, $"\n## {speaker}, {Time(message.CreatedAt)}\n\n");

        foreach (var note in Notes(message, botName))
        {
            text.Append(CultureInfo.InvariantCulture, $"_{note}_\n\n");
        }

        if (message.Text.Length > 0)
        {
            text.Append(message.Text.TrimEnd()).Append("\n\n");
        }

        if (message.ToolCalls.Count > 0)
        {
            text.Append("Tools used:\n\n");
            foreach (var call in message.ToolCalls)
            {
                text.Append(CultureInfo.InvariantCulture, $"- `{Code(call.Name)}`: {OutcomeWords(call.Outcome)}\n");
            }

            text.Append('\n');
        }

        if (message.Attachments.Count > 0)
        {
            text.Append("Files:\n\n");
            foreach (var file in message.Attachments)
            {
                text.Append(CultureInfo.InvariantCulture, $"- {Inline(file.Name)} ({Inline(file.MediaType)}, {Size(file.Size)})\n");
            }

            text.Append('\n');
        }
    }

    private static IEnumerable<string> Notes(ExportedMessage message, string botName)
    {
        if (message.SentBySchedule)
        {
            yield return "Sent by a routine.";
        }

        if (message.Withheld)
        {
            yield return $"A rule of {Inline(botName)} kept this message from the bot.";
        }

        if (message.Pending)
        {
            yield return $"Waiting for {Inline(botName)} to take it.";
        }
    }

    private static string OutcomeWords(string outcome) => outcome switch
    {
        ExportedToolOutcomes.Succeeded => "succeeded",
        ExportedToolOutcomes.Failed => "failed",
        ExportedToolOutcomes.Denied => "declined",
        ExportedToolOutcomes.Interrupted => "interrupted",
        ExportedToolOutcomes.AwaitingApproval => "waiting for approval",
        ExportedToolOutcomes.Running => "still running",
        _ => "outcome not known",
    };

    private static string Time(DateTimeOffset at) =>
        string.Create(CultureInfo.InvariantCulture, $"{at.UtcDateTime:yyyy-MM-dd HH:mm} UTC");

    private static string Size(long bytes) => bytes switch
    {
        1 => "1 byte",
        < Kilobyte => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
        < Megabyte => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)Kilobyte:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)Megabyte:0.#} MB"),
    };

    /// <summary>A name on one line, with every sign that Markdown reads escaped, so it stays plain text.</summary>
    private static string Inline(string name)
    {
        var line = OneLine(name);
        var escaped = new StringBuilder(line.Length * 2);
        foreach (var letter in line)
        {
            if (MarkdownSigns.Contains(letter))
            {
                escaped.Append('\\');
            }

            escaped.Append(letter);
        }

        return escaped.ToString();
    }

    /// <summary>A name inside a code span: on one line, with no backtick that could close the span.</summary>
    private static string Code(string name) => OneLine(name).Replace('`', '\'');

    private static string OneLine(string name) =>
        string.Join(' ', name.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    #endregion
}
