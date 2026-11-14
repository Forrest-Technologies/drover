namespace Drover.Client;

/// <summary>
/// The bot editor's calls: a bot's whole definition, its tools, and its schedules. The server allows them to a
/// space's owners and admins only.
/// </summary>
public sealed partial class DroverApi
{
    #region Definition

    public Task<BotDetail> GetBotDefinition(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<BotDetail>($"{Space(spaceId)}/bots/{botId}/definition", cancellationToken);

    public Task<BotDetail> UpdateBotDefinition(Guid spaceId, Guid botId, UpdateBotRequest request, CancellationToken cancellationToken = default) =>
        Send<BotDetail>(HttpMethod.Put, $"{Space(spaceId)}/bots/{botId}/definition", request, cancellationToken);

    public Task<BotDetail> DuplicateBot(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Post<BotDetail>($"{Space(spaceId)}/bots/{botId}/duplicate", null, cancellationToken);

    public Task<BotDetail> PauseBot(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Post<BotDetail>($"{Space(spaceId)}/bots/{botId}/pause", null, cancellationToken);

    public Task<BotDetail> ResumeBot(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Post<BotDetail>($"{Space(spaceId)}/bots/{botId}/resume", null, cancellationToken);

    public Task RemoveBot(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"{Space(spaceId)}/bots/{botId}", cancellationToken);

    #endregion

    #region Tools

    public Task<List<BotToolEntry>> ListBotTools(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<List<BotToolEntry>>($"{Space(spaceId)}/bots/{botId}/tools", cancellationToken);

    /// <summary>Sets one tool for the bot to "off", "ask", or "allow". It takes effect at once.</summary>
    public Task<BotToolEntry> SetBotToolGrant(Guid spaceId, Guid botId, Guid toolId, string grant, CancellationToken cancellationToken = default) =>
        Send<BotToolEntry>(HttpMethod.Put, $"{Space(spaceId)}/bots/{botId}/tools/{toolId}", new SetToolGrantRequest(grant), cancellationToken);

    #endregion

    #region Schedules

    public Task<List<BotScheduleResponse>> ListBotSchedules(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<List<BotScheduleResponse>>($"{Space(spaceId)}/bots/{botId}/schedules", cancellationToken);

    public Task<BotScheduleResponse> CreateBotSchedule(Guid spaceId, Guid botId, BotScheduleBody schedule, CancellationToken cancellationToken = default) =>
        Post<BotScheduleResponse>($"{Space(spaceId)}/bots/{botId}/schedules", schedule, cancellationToken);

    public Task<BotScheduleResponse> UpdateBotSchedule(Guid spaceId, Guid botId, Guid scheduleId, BotScheduleBody schedule, CancellationToken cancellationToken = default) =>
        Send<BotScheduleResponse>(HttpMethod.Put, $"{Space(spaceId)}/bots/{botId}/schedules/{scheduleId}", schedule, cancellationToken);

    public Task RemoveBotSchedule(Guid spaceId, Guid botId, Guid scheduleId, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"{Space(spaceId)}/bots/{botId}/schedules/{scheduleId}", cancellationToken);

    #endregion

    #region Rules

    /// <summary>The rules the bot follows: those of the bots above it first, then its own, in order.</summary>
    public Task<List<BotRuleResponse>> ListBotRules(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<List<BotRuleResponse>>($"{Space(spaceId)}/bots/{botId}/rules", cancellationToken);

    public Task<BotRuleResponse> CreateBotRule(Guid spaceId, Guid botId, BotRuleBody rule, CancellationToken cancellationToken = default) =>
        Post<BotRuleResponse>($"{Space(spaceId)}/bots/{botId}/rules", rule, cancellationToken);

    public Task<BotRuleResponse> UpdateBotRule(Guid spaceId, Guid botId, Guid ruleId, BotRuleBody rule, CancellationToken cancellationToken = default) =>
        Send<BotRuleResponse>(HttpMethod.Put, $"{Space(spaceId)}/bots/{botId}/rules/{ruleId}", rule, cancellationToken);

    public Task RemoveBotRule(Guid spaceId, Guid botId, Guid ruleId, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"{Space(spaceId)}/bots/{botId}/rules/{ruleId}", cancellationToken);

    /// <summary>What a sandbox on this server may be.</summary>
    public Task<BotSandboxOptions> GetBotSandboxOptions(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<BotSandboxOptions>($"{Space(spaceId)}/bots/{botId}/sandbox/options", cancellationToken);

    /// <summary>What a rule on this server may use.</summary>
    public Task<BotRuleOptions> GetBotRuleOptions(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<BotRuleOptions>($"{Space(spaceId)}/bots/{botId}/rules/options", cancellationToken);

    /// <summary>What the bot's rules would do with a sample. The server decides, with the evaluator a run uses.</summary>
    public Task<TryRulesResponse> TryBotRules(Guid spaceId, Guid botId, TryRulesRequest sample, CancellationToken cancellationToken = default) =>
        Post<TryRulesResponse>($"{Space(spaceId)}/bots/{botId}/rules/try", sample, cancellationToken);

    #endregion
}
