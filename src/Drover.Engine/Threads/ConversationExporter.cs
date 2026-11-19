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
    TimeProvider time)
{
    #region Private Fields

    private const string FallbackBotName = "Bot";

    #endregion

    #region Public Methods

    /// <summary>The conversation, or null when the caller may not read the thread.</summary>
    public async Task<ConversationExport?> Export(Guid threadId, CancellationToken cancellationToken)
    {
        if (await threads.FindThread(threadId, cancellationToken) is not { } thread)
        {
            return null;
        }

        var bot = await bots.FindBot(thread.BotId, cancellationToken);
        IReadOnlyList<Message> all =
        [
            .. await messages.ListMessages(threadId, cancellationToken),
            .. await messages.ListPendingMessages(threadId, cancellationToken),
        ];
        var calls = await ListCalls(all, cancellationToken);

        return new ConversationExport(
            ConversationExport.CurrentVersion,
            thread.Id,
            thread.BotId,
            bot?.Name ?? FallbackBotName,
            thread.Title,
            thread.IsMain,
            thread.AudienceUserId == Audience.Everyone,
            time.GetUtcNow(),
            [.. all.Where(IsShown).Select(message => ToExported(message, calls)).Where(HasContent)]);
    }

    #endregion

    #region Private Methods

    private static bool IsShown(Message message) => message.Role is MessageRole.User or MessageRole.Assistant;

    private static bool HasContent(ExportedMessage message) =>
        message.Text.Length > 0 || message.ToolCalls.Count > 0 || message.Attachments.Count > 0;

    private static ExportedMessage ToExported(Message message, Dictionary<Guid, ExportedToolCall> calls) =>
        new(
            message.Position,
            message.Role is MessageRole.User ? "user" : "assistant",
            message.CreatedAt,
            string.Join("\n\n", message.Parts.OfType<TextPart>().Select(part => part.Text).Where(text => !string.IsNullOrWhiteSpace(text))),
            [.. message.Parts.OfType<ToolCallPart>().Select(call => calls[call.ToolCallId])],
            [.. message.Parts.Select(Attachment).OfType<ExportedAttachment>()],
            message.Position is null,
            message.Withheld,
            message.ScheduleId is not null);

    private static ExportedAttachment? Attachment(MessagePart part) => part switch
    {
        ImagePart image => new(image.Name ?? "picture", image.MediaType, image.Size),
        FilePart file => new(file.Name, file.MediaType, file.Size),
        _ => null,
    };

    /// <summary>
    /// Each call in the messages by its id: named as its row names the tool, or as the model did when the row is gone,
    /// with how it ended as its row says, or else as its result in the thread says.
    /// </summary>
    private async Task<Dictionary<Guid, ExportedToolCall>> ListCalls(IReadOnlyList<Message> all, CancellationToken cancellationToken)
    {
        var parts = all.SelectMany(message => message.Parts).ToList();
        var asked = parts.OfType<ToolCallPart>().GroupBy(call => call.ToolCallId).ToDictionary(group => group.Key, group => group.First());
        var rows = asked.Count == 0
            ? []
            : (await toolCalls.ListToolCalls([.. asked.Keys], cancellationToken)).ToDictionary(call => call.Id);
        var results = parts.OfType<ToolResultPart>()
            .GroupBy(result => result.ToolCallId)
            .ToDictionary(group => group.Key, group => group.Last());

        return asked.ToDictionary(
            call => call.Key,
            call => rows.GetValueOrDefault(call.Key) is { } row
                ? new ExportedToolCall(row.ToolName, Outcome(row, results.GetValueOrDefault(call.Key)))
                : new ExportedToolCall(call.Value.ToolName, Outcome(null, results.GetValueOrDefault(call.Key))));
    }

    private static string Outcome(ToolCall? row, ToolResultPart? result) => row?.Status switch
    {
        ToolCallStatus.Completed => row.IsError ? ExportedToolOutcomes.Failed : ExportedToolOutcomes.Succeeded,
        ToolCallStatus.Failed => ExportedToolOutcomes.Failed,
        ToolCallStatus.Denied => ExportedToolOutcomes.Denied,
        ToolCallStatus.Interrupted => ExportedToolOutcomes.Interrupted,
        ToolCallStatus.PendingApproval => ExportedToolOutcomes.AwaitingApproval,
        ToolCallStatus.Approved or ToolCallStatus.Running => ExportedToolOutcomes.Running,
        _ => result switch
        {
            null => ExportedToolOutcomes.Unknown,
            { IsError: true } => ExportedToolOutcomes.Failed,
            _ => ExportedToolOutcomes.Succeeded,
        },
    };

    #endregion
}
