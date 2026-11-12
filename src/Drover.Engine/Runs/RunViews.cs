namespace Drover.Engine.Runs;

/// <summary>Maps the engine's rows to what the API and the event log carry. Nothing here can reach a secret.</summary>
public static class RunViews
{
    #region Public Methods

    public static string ToWireName(this RunStatus status) => status.ToString().ToLowerInvariant();

    public static RunSummary ToSummary(this Run run) =>
        new(
            run.Id,
            run.ThreadId,
            run.BotId,
            run.RootRunId,
            run.Status.ToWireName(),
            run.StatusReason,
            run.Error is { } error ? new RunProblem(error.Code, error.Message) : null,
            run.Attempt,
            run.LastEventSeq,
            run.Usage.ToTotals(),
            run.StartedAt,
            run.FinishedAt);

    public static UsageTotals ToTotals(this RunUsage usage) =>
        new(usage.InputTokens, usage.OutputTokens, usage.CachedTokens, usage.Cost);

    public static RunEventResponse ToResponse(this RunEvent stored) =>
        new(stored.Seq, stored.RunId, stored.Type, stored.Data.GetRawText(), stored.CreatedAt);

    public static BotSummary ToSummary(this Bot bot) =>
        new(
            bot.Id,
            bot.Name,
            bot.Handle,
            bot.Instructions,
            bot.ModelConnectionId,
            bot.Model,
            bot.Status.ToString().ToLowerInvariant());

    public static ThreadSummary ToSummary(this ChatThread thread, Guid? activeRunId) =>
        new(
            thread.Id,
            thread.BotId,
            thread.Title,
            thread.AudienceUserId == Audience.Everyone,
            thread.LastActivityAt,
            activeRunId);

    public static MessageResponse ToResponse(this Message message) =>
        new(
            message.Id,
            message.ThreadId,
            message.Position,
            message.Role.ToString().ToLowerInvariant(),
            message.RunId,
            [.. message.Parts.Select(ToResponse)],
            message.CreatedAt,
            message.Steers);

    #endregion

    #region Private Methods

    private static MessagePartResponse ToResponse(MessagePart part) => part switch
    {
        TextPart text => new("text") { Text = text.Text },
        ReasoningPart reasoning => new("reasoning") { Text = reasoning.Text },
        ToolCallPart call => new("tool_call") { ToolCallId = call.ToolCallId, ToolName = call.ToolName, Arguments = call.Arguments.GetRawText() },
        ToolResultPart result => new("tool_result") { ToolCallId = result.ToolCallId, Result = result.Result.GetRawText(), IsError = result.IsError },
        ImagePart image => new("image") { FileId = image.FileId, MediaType = image.MediaType, Text = image.AltText },
        FilePart file => new("file") { FileId = file.FileId, MediaType = file.MediaType, Text = file.Name },
        _ => new("unknown"),
    };

    #endregion
}
