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
        Send(HttpMethod.Delete, $"{Space(spaceId)}/bots/{botId}", cancellationToken);

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
        Send(HttpMethod.Delete, $"{Space(spaceId)}/bots/{botId}/schedules/{scheduleId}", cancellationToken);

    #endregion
}
