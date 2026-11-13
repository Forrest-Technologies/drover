namespace Drover.Engine.Bots;

/// <summary>
/// Bots, their tools, and their schedules as the API returns them, and requests read into Drover's own types.
/// </summary>
public static class BotViews
{
    #region Public Methods

    public static BotDetail ToDetail(this BotView view)
    {
        var bot = view.Bot;
        return new(
            bot.Id,
            bot.Name,
            bot.Handle,
            new BotAvatarBody(bot.Avatar.Emoji, bot.Avatar.FileId),
            bot.DescriptionForBots,
            bot.Instructions,
            bot.ModelConnectionId,
            bot.Model,
            new ModelSettingsBody(
                bot.ModelSettings.Temperature,
                bot.ModelSettings.MaxOutputTokens,
                bot.ModelSettings.ReasoningEffort,
                Words.Of(bot.ModelSettings.WebSearch),
                bot.ModelSettings.StructuredOutput),
            new MemorySettingsBody(
                bot.MemorySettings.Enabled,
                Words.Of(bot.MemorySettings.Compaction),
                bot.MemorySettings.CompactAtTokens,
                bot.MemorySettings.KeepRecentMessages),
            Words.Of(bot.SandboxPolicy),
            new SandboxSettingsBody(Words.Of(bot.SandboxSettings.Network), bot.SandboxSettings.AllowedHosts, bot.SandboxSettings.IdleTimeoutMinutes),
            view.SandboxSharedWith,
            Words.Of(bot.Status),
            bot.ParentBotId,
            bot.Depth,
            new BuildLimitsBody(bot.BuildLimits.MaxChildren, bot.BuildLimits.MaxDepthBelow),
            bot.CreatedByUserId,
            bot.CreatedByBotId);
    }

    /// <summary>
    /// The request as an edit. A part the request leaves out keeps the bot's stored value, so a client that knows
    /// only some parts cannot wipe the others; a word that names nothing becomes null, which the service refuses.
    /// </summary>
    public static BotEdit ToEdit(this UpdateBotRequest request, BotView current)
    {
        var bot = current.Bot;
        return new(
            request.Name ?? bot.Name,
            request.Handle ?? bot.Handle,
            request.Avatar is { } avatar ? new BotAvatar { Emoji = string.IsNullOrWhiteSpace(avatar.Emoji) ? null : avatar.Emoji, FileId = avatar.FileId } : bot.Avatar,
            request.DescriptionForBots ?? bot.DescriptionForBots,
            request.Instructions ?? bot.Instructions,
            request.ModelConnectionId ?? bot.ModelConnectionId,
            request.Model ?? bot.Model,
            request.ModelSettings is { } model ? Read(model) : bot.ModelSettings,
            request.MemorySettings is { } memory ? Read(memory) : bot.MemorySettings,
            request.SandboxPolicy is { } policy ? Words.Read<SandboxPolicy>(policy) : bot.SandboxPolicy,
            request.SandboxSettings is { } sandbox ? Read(sandbox) : bot.SandboxSettings,
            request.SandboxSharedWith ?? current.SandboxSharedWith,
            request.BuildLimits is { } limits ? new BuildLimits { MaxChildren = limits.MaxChildren, MaxDepthBelow = limits.MaxDepthBelow } : bot.BuildLimits);
    }

    public static BotToolEntry ToEntry(this BotToolView view)
    {
        var reviewed = view.NeedsReview && view.Tool.ReviewedDefinition is { } kept ? kept.Deserialize<ReviewedToolDefinition>() : null;
        return new(
            view.Tool.Id,
            view.Tool.Name,
            string.IsNullOrWhiteSpace(view.Tool.Title) ? view.Tool.Name : view.Tool.Title,
            view.Tool.Description,
            view.Tool.SourceKind is ToolSourceKind.Builtin ? "builtin" : "connection",
            view.Connection?.Id,
            view.Connection?.Name,
            Words.Of(view.Tool.Sensitivity),
            GrantService.Word(view.Own),
            GrantService.Word(view.Ceiling),
            view.CeilingBotName,
            Words.Of(view.Tool.State))
        {
            NeedsReview = view.NeedsReview,
            Reviewed = reviewed is null ? null : new ToolDefinitionSnapshot(reviewed.Title, reviewed.Description, reviewed.InputSchema),
        };
    }

    public static BotScheduleResponse ToResponse(this BotSchedule schedule) =>
        new(
            schedule.Id,
            schedule.BotId,
            schedule.OwnerUserId,
            schedule.Cron,
            schedule.TimeZone,
            schedule.Prompt,
            Words.Of(schedule.ThreadMode),
            schedule.Enabled,
            schedule.NextRunAt);

    public static ScheduleEdit ToEdit(this BotScheduleBody body) =>
        new(body.Cron ?? string.Empty, body.TimeZone ?? string.Empty, body.Prompt ?? string.Empty, Words.Read<ScheduleThreadMode>(body.ThreadMode), body.Enabled);

    #endregion

    #region Private Methods

    private static ModelSettings? Read(ModelSettingsBody body) =>
        Words.Read<WebSearchMode>(body.WebSearch) is { } webSearch
            ? new ModelSettings
            {
                Temperature = body.Temperature,
                MaxOutputTokens = body.MaxOutputTokens,
                ReasoningEffort = string.IsNullOrWhiteSpace(body.ReasoningEffort) ? null : body.ReasoningEffort.Trim(),
                WebSearch = webSearch,
                StructuredOutput = body.StructuredOutput,
            }
            : null;

    private static MemorySettings? Read(MemorySettingsBody body) =>
        Words.Read<CompactionStrategy>(body.Compaction) is { } compaction
            ? new MemorySettings
            {
                Enabled = body.Enabled,
                Compaction = compaction,
                CompactAtTokens = body.CompactAtTokens,
                KeepRecentMessages = body.KeepRecentMessages,
            }
            : null;

    private static SandboxSettings? Read(SandboxSettingsBody body) =>
        Words.Read<NetworkAccess>(body.Network) is { } network
            ? new SandboxSettings { Network = network, AllowedHosts = body.AllowedHosts ?? [], IdleTimeoutMinutes = body.IdleTimeoutMinutes }
            : null;

    #endregion
}
