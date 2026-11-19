namespace Drover.Engine.Threads;

/// <summary>
/// An exported conversation as a file a person keeps: Markdown to read, or the versioned JSON contract to process.
/// Names a person or a bot chose are written so they cannot change the document's shape; the words of the
/// conversation are written as they were said, because they are Markdown already.
/// </summary>
public static partial class ConversationExportFiles
{
    #region Private Fields

    private const string MarkdownSigns = "\\`*_[]<>#|~!";

    private const long Kilobyte = 1024;

    private const long Megabyte = 1024 * Kilobyte;

    #endregion

    #region Public Methods

    public static byte[] ToJson(this ConversationExport export)
    {
        using var buffer = new MemoryStream();
        using var writer = CreateJsonWriter(buffer);
        WriteJsonHeader(
            writer,
            new ConversationExportMetadata(
                export.ThreadId,
                export.BotId,
                export.BotName,
                export.Title,
                export.IsMain,
                export.IsShared,
                export.ExportedAt),
            export.Version);

        foreach (var message in export.Messages)
        {
            WriteJsonMessage(writer, message);
        }

        WriteJsonFooter(writer);
        writer.Flush();
        return buffer.ToArray();
    }

    public static byte[] ToMarkdown(this ConversationExport export)
    {
        using var buffer = new MemoryStream();
        using (var writer = new StreamWriter(buffer, new UTF8Encoding(false), leaveOpen: true))
        {
            WriteMarkdownHeader(
                writer,
                new ConversationExportMetadata(
                    export.ThreadId,
                    export.BotId,
                    export.BotName,
                    export.Title,
                    export.IsMain,
                    export.IsShared,
                    export.ExportedAt),
                export.Version);

            foreach (var message in export.Messages)
            {
                WriteMarkdownMessage(writer, message, export.BotName);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>A name for the saved file: the bot's name and the day, with the format's extension.</summary>
    public static string FileName(this ConversationExport export, string format) =>
        FileName(export.BotName, export.ExportedAt, format);

    public static string FileName(string botName, DateTimeOffset exportedAt, string format)
    {
        var name = new string([.. botName.Where(letter => char.IsLetterOrDigit(letter) || letter is ' ' or '-' or '_')]).Trim();
        var extension = format is ExportFormats.Json ? "json" : "md";
        return $"{(name.Length > 0 ? name : "Drover")} conversation {exportedAt.UtcDateTime:yyyy-MM-dd}.{extension}";
    }

    internal static Utf8JsonWriter CreateJsonWriter(Stream destination) =>
        new(destination, new JsonWriterOptions { Indented = true });

    internal static Utf8JsonWriter CreateJsonWriter(IBufferWriter<byte> destination) =>
        new(destination, new JsonWriterOptions { Indented = true });

    internal static void WriteJsonHeader(Utf8JsonWriter writer, ConversationExportMetadata metadata, int version)
    {
        writer.WriteStartObject();
        writer.WriteNumber("version", version);
        writer.WriteString("threadId", metadata.ThreadId);
        writer.WriteString("botId", metadata.BotId);
        writer.WriteString("botName", metadata.BotName);

        if (metadata.Title is null)
        {
            writer.WriteNull("title");
        }
        else
        {
            writer.WriteString("title", metadata.Title);
        }

        writer.WriteBoolean("isMain", metadata.IsMain);
        writer.WriteBoolean("isShared", metadata.IsShared);
        writer.WriteString("exportedAt", metadata.ExportedAt);
        writer.WriteStartArray("messages");
    }

    internal static void WriteJsonMessage(Utf8JsonWriter writer, ExportedMessage message)
    {
        writer.WriteStartObject();

        if (message.Position is { } position)
        {
            writer.WriteNumber("position", position);
        }
        else
        {
            writer.WriteNull("position");
        }

        writer.WriteString("role", message.Role);
        writer.WriteString("createdAt", message.CreatedAt);
        writer.WriteString("text", message.Text);
        writer.WriteStartArray("toolCalls");

        foreach (var call in message.ToolCalls)
        {
            writer.WriteStartObject();
            writer.WriteString("name", call.Name);
            writer.WriteString("outcome", call.Outcome);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("attachments");

        foreach (var file in message.Attachments)
        {
            writer.WriteStartObject();
            writer.WriteString("name", file.Name);
            writer.WriteString("mediaType", file.MediaType);
            writer.WriteNumber("size", file.Size);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteBoolean("pending", message.Pending);
        writer.WriteBoolean("withheld", message.Withheld);
        writer.WriteBoolean("sentBySchedule", message.SentBySchedule);
        writer.WriteEndObject();
    }

    internal static void WriteJsonFooter(Utf8JsonWriter writer)
    {
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal static void WriteMarkdownHeader(TextWriter writer, ConversationExportMetadata metadata, int version)
    {
        writer.Write("# Conversation with ");
        writer.Write(Inline(metadata.BotName));
        writer.Write("\n\n");

        if (metadata.Title is { Length: > 0 } title)
        {
            writer.Write(Inline(title));
            writer.Write("\n\n");
        }

        writer.Write("Saved ");
        writer.Write(Time(metadata.ExportedAt));
        writer.Write(". Export format version ");
        writer.Write(version);
        writer.Write(".\n");
    }

    internal static void WriteMarkdownMessage(TextWriter writer, ExportedMessage message, string botName)
    {
        var speaker = message.Role is "user" ? "Person" : Inline(botName);
        writer.Write("\n## ");
        writer.Write(speaker);
        writer.Write(", ");
        writer.Write(Time(message.CreatedAt));
        writer.Write("\n\n");

        foreach (var note in Notes(message, botName))
        {
            writer.Write('_');
            writer.Write(note);
            writer.Write("_\n\n");
        }

        if (message.Text.Length > 0)
        {
            writer.Write(NeutralizeMarkdown(message.Text.TrimEnd()));
            writer.Write("\n\n");
        }

        if (message.ToolCalls.Count > 0)
        {
            writer.Write("Tools used:\n\n");
            foreach (var call in message.ToolCalls)
            {
                writer.Write("- `");
                writer.Write(Code(call.Name));
                writer.Write("`: ");
                writer.Write(OutcomeWords(call.Outcome));
                writer.Write('\n');
            }

            writer.Write('\n');
        }

        if (message.Attachments.Count > 0)
        {
            writer.Write("Files:\n\n");
            foreach (var file in message.Attachments)
            {
                writer.Write("- ");
                writer.Write(Inline(file.Name));
                writer.Write(" (");
                writer.Write(Inline(file.MediaType));
                writer.Write(", ");
                writer.Write(Size(file.Size));
                writer.Write(")\n");
            }

            writer.Write('\n');
        }
    }

    /// <summary>
    /// Keeps ordinary Markdown syntax intact outside code blocks and inline code spans while neutralizing active HTML,
    /// remote or inline image embeds, and scriptable link schemes.
    /// </summary>
    public static string NeutralizeMarkdown(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var lines = text.Split('\n');
        char? fenceChar = null;
        var fenceLength = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var rawLine = lines[i];
            var hadCarriageReturn = rawLine.EndsWith('\r');
            var line = hadCarriageReturn ? rawLine[..^1] : rawLine;

            if (fenceChar is { } openChar)
            {
                if (IsClosingFence(line, openChar, fenceLength))
                {
                    fenceChar = null;
                    fenceLength = 0;
                }

                continue;
            }

            if (TryOpenFence(line, out var newChar, out var newLength))
            {
                fenceChar = newChar;
                fenceLength = newLength;
                continue;
            }

            var neutralized = NeutralizeLine(line);
            lines[i] = hadCarriageReturn ? $"{neutralized}\r" : neutralized;
        }

        return string.Join('\n', lines);
    }

    #endregion

    #region Private Methods

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

    private static string NeutralizeLine(string line)
    {
        var output = new StringBuilder(line.Length);
        var index = 0;

        while (index < line.Length)
        {
            var tick = line.IndexOf('`', index);

            if (tick < 0)
            {
                output.Append(NeutralizeProse(line[index..], index == 0));
                break;
            }

            if (tick > index)
            {
                output.Append(NeutralizeProse(line[index..tick], index == 0));
            }

            var runEnd = tick;

            while (runEnd < line.Length && line[runEnd] == '`')
            {
                runEnd++;
            }

            var fence = line[tick..runEnd];
            var close = FindClosingBackticks(line, runEnd, fence.Length);

            if (close >= 0)
            {
                var spanEnd = close + fence.Length;
                output.Append(line, tick, spanEnd - tick);
                index = spanEnd;
            }
            else
            {
                output.Append(line, tick, runEnd - tick);
                index = runEnd;
            }
        }

        return output.ToString();
    }

    private static string NeutralizeProse(string prose, bool atLineStart)
    {
        var sanitized = HtmlTagStart().Replace(prose, "&lt;");
        sanitized = ImageEmbed().Replace(sanitized, "$1\\![");
        sanitized = UnsafeLink().Replace(sanitized, "$1\\[");

        if (atLineStart)
        {
            sanitized = UnsafeReferenceLink().Replace(sanitized, "$1\\[$2]:");
        }

        return sanitized;
    }

    private static int FindClosingBackticks(string line, int start, int count)
    {
        var index = start;

        while (index < line.Length)
        {
            var found = line.IndexOf('`', index);

            if (found < 0)
            {
                return -1;
            }

            var end = found;

            while (end < line.Length && line[end] == '`')
            {
                end++;
            }

            if (end - found == count)
            {
                return found;
            }

            index = end;
        }

        return -1;
    }

    private static bool TryOpenFence(string line, out char fenceChar, out int length)
    {
        fenceChar = default;
        length = 0;
        var leadingSpaces = CountLeadingSpaces(line);

        if (leadingSpaces > 3 || leadingSpaces >= line.Length)
        {
            return false;
        }

        var marker = line[leadingSpaces];

        if (marker is not ('`' or '~'))
        {
            return false;
        }

        var count = 0;

        while (leadingSpaces + count < line.Length && line[leadingSpaces + count] == marker)
        {
            count++;
        }

        if (count < 3)
        {
            return false;
        }

        if (marker == '`' && line.AsSpan(leadingSpaces + count).Contains('`'))
        {
            return false;
        }

        fenceChar = marker;
        length = count;
        return true;
    }

    private static bool IsClosingFence(string line, char fenceChar, int minLength)
    {
        var leadingSpaces = CountLeadingSpaces(line);

        if (leadingSpaces > 3 || leadingSpaces >= line.Length)
        {
            return false;
        }

        var count = 0;

        while (leadingSpaces + count < line.Length && line[leadingSpaces + count] == fenceChar)
        {
            count++;
        }

        return count >= minLength && line.AsSpan(leadingSpaces + count).IsWhiteSpace();
    }

    private static int CountLeadingSpaces(string line)
    {
        var count = 0;

        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

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

    [GeneratedRegex("""<(?=(?:!--|!\[|!\w|\?|/?[A-Za-z][A-Za-z0-9-]*(?::[A-Za-z0-9-]+)*(?:[\s>]|/>|$)))""", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagStart();

    [GeneratedRegex("""(?:^|(?<=[^\\]))((?:\\\\)*)!\[""", RegexOptions.CultureInvariant)]
    private static partial Regex ImageEmbed();

    [GeneratedRegex("""(?:^|(?<=[^\\]))((?:\\\\)*)\[(?=[^\]\r\n]*\]\(\s*(?:(?:javascript|vbscript|data)\s*:|&#))""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeLink();

    [GeneratedRegex("""^(\s{0,3})\[([^\]\r\n]+)\]:(?=\s*(?:(?:javascript|vbscript|data)\s*:|&#))""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeReferenceLink();

    #endregion
}


internal sealed record ConversationExportMetadata(
    Guid ThreadId,
    Guid BotId,
    string BotName,
    string? Title,
    bool IsMain,
    bool IsShared,
    DateTimeOffset ExportedAt);
