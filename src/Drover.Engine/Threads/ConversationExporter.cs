namespace Drover.Engine.Threads;

/// <summary>
/// A conversation as a person saves it. Runs in the tenant scope the endpoint filter opened, where row-level security
/// shows a person their own threads and the shared ones, so a thread the caller may not read is not found. The thread's
/// messages, tool calls, and files all carry its audience, so nothing read here reaches past what the caller may read.
/// Only what the conversation shows is exported: a person's and the bot's words, each tool call by name and outcome,
/// and each file by name and size. Tool arguments, tool output, and reasoning are left out.
/// </summary>
public sealed class ConversationExporter(
    ThreadRepository threads,
    MessageRepository messages,
    ToolCallRepository toolCalls,
    BotRepository bots,
    TenantScope scope,
    TimeProvider time)
{
    #region Private Fields

    private const string FallbackBotName = "Bot";

    private const int PageSize = 200;

    private static readonly Redactor redactor = new([]);

    private static readonly JsonSerializerOptions partJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    #endregion

    #region Public Methods

    /// <summary>The conversation, or null when the caller may not read the thread.</summary>
    public async Task<ConversationExport?> Export(Guid threadId, CancellationToken cancellationToken)
    {
        if (await ReadMetadata(threadId, cancellationToken) is not { } metadata)
        {
            return null;
        }

        List<ExportedMessage> exported = [];
        int? afterPosition = null;

        while (true)
        {
            var page = await messages.ListExportPlacedMessages(threadId, afterPosition, PageSize, cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            exported.AddRange(await BuildPage(threadId, page, cancellationToken));

            if (page.Count < PageSize)
            {
                break;
            }

            afterPosition = page[^1].Position;
        }

        DateTimeOffset? afterCreatedAt = null;
        Guid? afterId = null;

        while (true)
        {
            var page = await messages.ListExportPendingMessages(threadId, afterCreatedAt, afterId, PageSize, cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            exported.AddRange(await BuildPage(threadId, page, cancellationToken));

            if (page.Count < PageSize)
            {
                break;
            }

            afterCreatedAt = page[^1].CreatedAt;
            afterId = page[^1].Id;
        }

        return new ConversationExport(
            ConversationExport.CurrentVersion,
            metadata.ThreadId,
            metadata.BotId,
            metadata.BotName,
            metadata.Title,
            metadata.IsMain,
            metadata.IsShared,
            metadata.ExportedAt,
            exported);
    }

    /// <summary>Opens a short scope to confirm thread access and read export metadata, or returns null when not found.</summary>
    public async Task<ConversationExportSession?> BeginExport(Guid threadId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);

        if (await ReadMetadata(threadId, cancellationToken) is not { } metadata)
        {
            return null;
        }

        await work.Commit(cancellationToken);
        return new ConversationExportSession(metadata);
    }

    /// <summary>
    /// Streams the conversation directly to <paramref name="destination"/> in bounded pages, opening a short tenant
    /// scope per page and closing it before writing bytes so no transaction stays open on the network.
    /// </summary>
    public async Task WriteExport(
        ConversationExportSession session,
        string format,
        Stream destination,
        CancellationToken cancellationToken)
    {
        if (format is ExportFormats.Json)
        {
            await WriteJson(session.Metadata, destination, cancellationToken);
        }
        else
        {
            await WriteMarkdown(session.Metadata, destination, cancellationToken);
        }
    }

    #endregion

    #region Streaming

    private async Task WriteJson(ConversationExportMetadata metadata, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        await using var writer = ConversationExportFiles.CreateJsonWriter(buffer);
        ConversationExportFiles.WriteJsonHeader(writer, metadata, ConversationExport.CurrentVersion);
        await writer.FlushAsync(cancellationToken);
        await FlushBuffer(buffer, destination, cancellationToken);

        await ForEachPage(
            metadata.ThreadId,
            async page =>
            {
                foreach (var message in page)
                {
                    ConversationExportFiles.WriteJsonMessage(writer, message);
                    await writer.FlushAsync(cancellationToken);

                    if (buffer.WrittenCount >= 16 * 1024)
                    {
                        await FlushBuffer(buffer, destination, cancellationToken);
                    }
                }

                await FlushBuffer(buffer, destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            },
            cancellationToken);

        ConversationExportFiles.WriteJsonFooter(writer);
        await writer.FlushAsync(cancellationToken);
        await FlushBuffer(buffer, destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private async Task WriteMarkdown(ConversationExportMetadata metadata, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new StringWriter();
        ConversationExportFiles.WriteMarkdownHeader(buffer, metadata, ConversationExport.CurrentVersion);
        await FlushText(buffer, destination, cancellationToken);

        await ForEachPage(
            metadata.ThreadId,
            async page =>
            {
                foreach (var message in page)
                {
                    ConversationExportFiles.WriteMarkdownMessage(buffer, message, metadata.BotName);
                    await FlushText(buffer, destination, cancellationToken);
                }

                await destination.FlushAsync(cancellationToken);
            },
            cancellationToken);
    }

    private static async Task FlushBuffer(ArrayBufferWriter<byte> buffer, Stream destination, CancellationToken cancellationToken)
    {
        if (buffer.WrittenCount == 0)
        {
            return;
        }

        await destination.WriteAsync(buffer.WrittenMemory, cancellationToken);
        buffer.Clear();
    }

    private static async Task FlushText(StringWriter buffer, Stream destination, CancellationToken cancellationToken)
    {
        var content = buffer.GetStringBuilder();

        if (content.Length == 0)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(content.ToString());
        content.Clear();
        await destination.WriteAsync(bytes, cancellationToken);
    }

    private async Task ForEachPage(
        Guid threadId,
        Func<List<ExportedMessage>, Task> onPage,
        CancellationToken cancellationToken)
    {
        int? afterPosition = null;

        while (true)
        {
            List<ExportedMessage> page;
            bool done;

            await using (var work = await scope.Begin(cancellationToken))
            {
                var rows = await messages.ListExportPlacedMessages(threadId, afterPosition, PageSize, cancellationToken);
                page = rows.Count == 0 ? [] : await BuildPage(threadId, rows, cancellationToken);
                done = rows.Count < PageSize;

                if (rows.Count > 0)
                {
                    afterPosition = rows[^1].Position;
                }

                await work.Commit(cancellationToken);
            }

            if (page.Count > 0)
            {
                await onPage(page);
            }

            if (done)
            {
                break;
            }
        }

        DateTimeOffset? afterCreatedAt = null;
        Guid? afterId = null;

        while (true)
        {
            List<ExportedMessage> page;
            bool done;

            await using (var work = await scope.Begin(cancellationToken))
            {
                var rows = await messages.ListExportPendingMessages(threadId, afterCreatedAt, afterId, PageSize, cancellationToken);
                page = rows.Count == 0 ? [] : await BuildPage(threadId, rows, cancellationToken);
                done = rows.Count < PageSize;

                if (rows.Count > 0)
                {
                    afterCreatedAt = rows[^1].CreatedAt;
                    afterId = rows[^1].Id;
                }

                await work.Commit(cancellationToken);
            }

            if (page.Count > 0)
            {
                await onPage(page);
            }

            if (done)
            {
                break;
            }
        }
    }

    #endregion

    #region Private Methods

    private async Task<ConversationExportMetadata?> ReadMetadata(Guid threadId, CancellationToken cancellationToken)
    {
        if (await threads.FindThread(threadId, cancellationToken) is not { } thread)
        {
            return null;
        }

        var bot = await bots.FindBot(thread.BotId, cancellationToken);
        var botName = redactor.Redact(bot?.Name ?? FallbackBotName);
        var title = thread.Title is { Length: > 0 } text ? redactor.Redact(text) : thread.Title;
        return new ConversationExportMetadata(
            thread.Id,
            thread.BotId,
            botName,
            title,
            thread.IsMain,
            thread.AudienceUserId == Audience.Everyone,
            time.GetUtcNow());
    }

    private async Task<List<ExportedMessage>> BuildPage(
        Guid threadId,
        List<ExportMessageRow> rows,
        CancellationToken cancellationToken)
    {
        List<(ExportMessageRow Row, List<ProjectedPart> Parts)> parsed = [];
        HashSet<Guid> askedIds = [];

        foreach (var row in rows)
        {
            var parts = JsonSerializer.Deserialize<List<ProjectedPart>>(row.PartsJson, partJson) ?? [];
            parsed.Add((row, parts));

            foreach (var part in parts)
            {
                if (part.Type is "tool_call" && part.ToolCallId is { } id)
                {
                    askedIds.Add(id);
                }
            }
        }

        var callRows = askedIds.Count == 0
            ? []
            : (await toolCalls.ListExportToolCalls(askedIds, cancellationToken)).ToDictionary(call => call.Id);
        List<Guid> missingIds = [.. askedIds.Where(id => !callRows.ContainsKey(id))];
        var fallbackResults = missingIds.Count == 0
            ? []
            : await messages.ListToolResultErrors(threadId, missingIds, cancellationToken);

        List<ExportedMessage> exported = [];

        foreach (var (row, parts) in parsed)
        {
            var message = ToExported(row, parts, callRows, fallbackResults);

            if (HasContent(message))
            {
                exported.Add(message);
            }
        }

        return exported;
    }

    private static bool HasContent(ExportedMessage message) =>
        message.Text.Length > 0 || message.ToolCalls.Count > 0 || message.Attachments.Count > 0;

    private static ExportedMessage ToExported(
        ExportMessageRow row,
        List<ProjectedPart> parts,
        Dictionary<Guid, ExportToolCallRow> calls,
        Dictionary<Guid, bool> fallbackResults)
    {
        var rawText = string.Join(
            "\n\n",
            parts
                .Where(part => part.Type is "text" && !string.IsNullOrWhiteSpace(part.Text))
                .Select(part => part.Text!));
        var text = rawText.Length > 0 ? redactor.Redact(rawText) : string.Empty;

        List<ExportedToolCall> exportedCalls = [];
        List<ExportedAttachment> attachments = [];

        foreach (var part in parts)
        {
            switch (part.Type)
            {
                case "tool_call" when part.ToolCallId is { } callId:
                    if (calls.TryGetValue(callId, out var found))
                    {
                        exportedCalls.Add(new(found.ToolName, Outcome(found, null)));
                    }
                    else
                    {
                        var hasResult = fallbackResults.TryGetValue(callId, out var isError);
                        exportedCalls.Add(new(part.ToolName ?? string.Empty, Outcome(null, hasResult ? isError : null)));
                    }

                    break;

                case "image":
                    attachments.Add(new(redactor.Redact(part.Name ?? "picture"), part.MediaType ?? string.Empty, part.Size ?? 0));
                    break;

                case "file":
                    attachments.Add(new(redactor.Redact(part.Name ?? string.Empty), part.MediaType ?? string.Empty, part.Size ?? 0));
                    break;
            }
        }

        return new ExportedMessage(
            row.Position,
            row.Role is "user" ? "user" : "assistant",
            row.CreatedAt,
            text,
            exportedCalls,
            attachments,
            row.Position is null,
            row.Withheld,
            row.ScheduleId is not null);
    }

    private static string Outcome(ExportToolCallRow? row, bool? fallbackIsError) => row?.Status switch
    {
        ToolCallStatus.Completed => row.IsError ? ExportedToolOutcomes.Failed : ExportedToolOutcomes.Succeeded,
        ToolCallStatus.Failed => ExportedToolOutcomes.Failed,
        ToolCallStatus.Denied => ExportedToolOutcomes.Denied,
        ToolCallStatus.Interrupted => ExportedToolOutcomes.Interrupted,
        ToolCallStatus.PendingApproval => ExportedToolOutcomes.AwaitingApproval,
        ToolCallStatus.Approved or ToolCallStatus.Running => ExportedToolOutcomes.Running,
        _ => fallbackIsError switch
        {
            null => ExportedToolOutcomes.Unknown,
            true => ExportedToolOutcomes.Failed,
            false => ExportedToolOutcomes.Succeeded,
        },
    };

    #endregion

    #region Nested Types

    private sealed record ProjectedPart(
        string Type,
        string? Text,
        string? Name,
        string? MediaType,
        long? Size,
        Guid? ToolCallId,
        string? ToolName);

    #endregion
}


/// <summary>An authorized export's metadata, captured in a short transaction before streaming starts.</summary>
public sealed class ConversationExportSession
{
    #region Constructors

    internal ConversationExportSession(ConversationExportMetadata metadata) => Metadata = metadata;

    #endregion

    #region Properties

    internal ConversationExportMetadata Metadata { get; }

    #endregion

    #region Public Methods

    public string FileName(string format) =>
        ConversationExportFiles.FileName(Metadata.BotName, Metadata.ExportedAt, format);

    #endregion
}
