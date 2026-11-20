namespace Drover.Engine.Threads;

/// <summary>
/// An exported conversation as a file a person keeps: Markdown to read, or the versioned JSON contract to process.
/// Names a person or a bot chose are written so they cannot change the document's shape; the words of the
/// conversation are written as they were said, because they are Markdown already.
/// </summary>
public static class ConversationExportFiles
{
    #region Private Fields

    private const string MarkdownSigns = "\\`*_[]<>#|~!";

    private const long Kilobyte = 1024;

    private const long Megabyte = 1024 * Kilobyte;

    private const int MaxSanitizePasses = 4;

    private static readonly MarkdownPipeline neutralizePipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .Build();

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
    /// remote or inline image embeds, and scriptable link schemes using Markdig's linear CommonMark parser.
    /// A reader resolves link reference definitions across the whole saved document, so a definition in one message
    /// can turn a reference in another into a link or an image. Each message is parsed alone and cannot see the
    /// others, so no definition is left standing and no reference is left to resolve: a definition is written as
    /// plain text, and an exclamation mark before a bracket is escaped so a reference-style image never embeds.
    /// </summary>
    public static string NeutralizeMarkdown(string text, ILogger? logger = null)
    {
        if (text.Length == 0)
        {
            return text;
        }

        if (ExceedsParserNesting(text))
        {
            return NeutralizeDeeplyNested(text);
        }

        var current = text;

        for (var pass = 0; pass < MaxSanitizePasses; pass++)
        {
            Markdig.Syntax.MarkdownDocument document;

            try
            {
                document = Markdig.Markdown.Parse(current, neutralizePipeline);
            }
            catch (ArgumentException tooDeep)
            {
                (logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
                    .LogWarning(tooDeep, "Markdown export exceeded parser nesting depth and used flat neutralization.");
                return NeutralizeDeeplyNested(current);
            }

            Dictionary<int, string> replacements = [];

            foreach (var block in document.Descendants<Block>())
            {
                if (block is HtmlBlock html)
                {
                    EscapeHtmlAngles(current, html.Span, replacements);
                }
                else if (block is LinkReferenceDefinition reference)
                {
                    EscapeDefinition(current, reference.Span, replacements);
                }
                else if (block is LeafBlock { Inline: { } root })
                {
                    List<SourceSpan> code = [];
                    CollectUnsafeInlines(current, root, replacements, code);
                    EscapeImageMarks(current, block.Span, code, replacements);
                }
            }

            if (replacements.Count == 0)
            {
                break;
            }

            current = ApplySingleCharReplacements(current, replacements);
        }

        return current;
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

    private static void CollectUnsafeInlines(string source, ContainerInline root, Dictionary<int, string> replacements, List<SourceSpan> code)
    {
        Stack<Markdig.Syntax.Inlines.Inline> stack = new();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();

            if (node is CodeInline)
            {
                code.Add(node.Span);
                continue;
            }

            if (node is HtmlInline html)
            {
                EscapeHtmlAngles(source, html.Span, replacements);
            }
            else if (node is AutolinkInline auto)
            {
                if (!auto.IsEmail
                    && !IsSafeLinkUrl(auto.Url)
                    && auto.Span.Start >= 0
                    && auto.Span.Start < source.Length
                    && source[auto.Span.Start] == '<')
                {
                    replacements[auto.Span.Start] = "&lt;";
                }
            }
            else if (node is LinkInline link)
            {
                var targetUrl = link.Url ?? link.Reference?.Url;

                if (link.IsImage)
                {
                    if (link.Span.Start >= 0 && link.Span.Start < source.Length && source[link.Span.Start] == '!')
                    {
                        replacements[link.Span.Start] = "\\!";
                    }

                    if (!IsSafeLinkUrl(targetUrl)
                        && link.Span.Start + 1 >= 0
                        && link.Span.Start + 1 < source.Length
                        && source[link.Span.Start + 1] == '[')
                    {
                        replacements[link.Span.Start + 1] = "\\[";
                    }
                }
                else if (!IsSafeLinkUrl(targetUrl)
                    && link.Span.Start >= 0
                    && link.Span.Start < source.Length
                    && source[link.Span.Start] == '[')
                {
                    replacements[link.Span.Start] = "\\[";
                }
            }

            if (node is ContainerInline container)
            {
                for (var child = container.FirstChild; child is not null; child = child.NextSibling)
                {
                    stack.Push(child);
                }
            }
        }
    }

    // A definition is "[label]: address". Escaping its opening bracket makes the line plain text, so it defines nothing.
    private static void EscapeDefinition(string source, SourceSpan span, Dictionary<int, string> replacements)
    {
        var end = Math.Min(source.Length - 1, span.End);

        for (var index = Math.Max(0, span.Start); index <= end; index++)
        {
            if (source[index] == '[')
            {
                replacements[index] = "\\[";
                return;
            }
        }
    }

    // An image written with a reference, "![alt][label]", "![alt][]" or "![alt]", is only text in this message, but
    // it becomes an embed wherever a definition for the label is read. The mark is escaped so it never can.
    // The code spans are sorted once and walked together with the text, so the work is the length of the text plus
    // the number of spans, however many spans and image marks a paragraph holds.
    private static void EscapeImageMarks(string source, SourceSpan span, List<SourceSpan> code, Dictionary<int, string> replacements)
    {
        code.Sort(static (first, second) => first.Start.CompareTo(second.Start));
        var end = Math.Min(source.Length - 2, span.End);
        var next = 0;

        for (var index = Math.Max(0, span.Start); index <= end; index++)
        {
            while (next < code.Count && code[next].End < index)
            {
                next++;
            }

            if (source[index] == '!'
                && source[index + 1] == '['
                && (index == 0 || source[index - 1] != '\\')
                && !(next < code.Count && code[next].Start <= index))
            {
                replacements[index] = "\\!";
            }
        }
    }

    private static void EscapeHtmlAngles(string source, SourceSpan span, Dictionary<int, string> replacements)
    {
        var start = Math.Max(0, span.Start);
        var end = Math.Min(source.Length - 1, span.End);

        for (var index = start; index <= end; index++)
        {
            if (source[index] == '<')
            {
                replacements[index] = "&lt;";
            }
        }
    }

    private static string ApplySingleCharReplacements(string source, Dictionary<int, string> replacements)
    {
        var indices = replacements.Keys.Order().ToArray();
        var output = new StringBuilder(source.Length + (indices.Length * 3));
        var cursor = 0;

        foreach (var index in indices)
        {
            if (index > cursor)
            {
                output.Append(source, cursor, index - cursor);
            }

            output.Append(replacements[index]);
            cursor = index + 1;
        }

        if (cursor < source.Length)
        {
            output.Append(source, cursor, source.Length - cursor);
        }

        return output.ToString();
    }

    private static bool IsSafeLinkUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        var decoded = WebUtility.HtmlDecode(url);
        Span<char> buffer = stackalloc char[Math.Min(decoded.Length, 64)];
        var written = 0;

        foreach (var ch in decoded)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                continue;
            }

            if (ch is '/' or '?' or '#')
            {
                return true;
            }

            if (ch == ':')
            {
                var scheme = buffer[..written];
                return scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                    || scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                    || scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase);
            }

            if (written < buffer.Length)
            {
                buffer[written++] = ch;
            }
        }

        return true;
    }

    private static bool ExceedsParserNesting(string source)
    {
        var openBrackets = 0;

        for (var index = 0; index < source.Length; index++)
        {
            var ch = source[index];

            if (ch == '[')
            {
                openBrackets++;

                if (openBrackets > 100)
                {
                    return true;
                }
            }
            else if (ch == ']' && openBrackets > 0)
            {
                openBrackets--;
            }
        }

        return false;
    }

    private static string NeutralizeDeeplyNested(string source)
    {
        var output = new StringBuilder(source.Length);
        var escaped = false;

        for (var index = 0; index < source.Length; index++)
        {
            var ch = source[index];

            if (escaped)
            {
                escaped = false;
                output.Append(ch);
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                output.Append(ch);
            }
            else if (ch == '<')
            {
                output.Append("&lt;");
            }
            else if (ch == '[')
            {
                output.Append("\\[");
            }
            else if (ch == '!' && index + 1 < source.Length && source[index + 1] == '[')
            {
                output.Append("\\!");
            }
            else
            {
                output.Append(ch);
            }
        }

        return output.ToString();
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
