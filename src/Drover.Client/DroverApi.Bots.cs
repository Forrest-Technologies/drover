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

    /// <summary>
    /// Removes the bot. A bot with bots beneath it needs <paramref name="removeBeneath"/>: true removes them with it,
    /// false keeps them and moves them up beneath its parent. Left null for such a bot, the server refuses.
    /// </summary>
    public Task RemoveBot(Guid spaceId, Guid botId, bool? removeBeneath = null, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"{Space(spaceId)}/bots/{botId}{BeneathQuery(removeBeneath)}", cancellationToken);

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

    public Task<ScheduleCheckResponse> CheckBotSchedule(Guid spaceId, Guid botId, ScheduleCheckBody time, CancellationToken cancellationToken = default) =>
        Post<ScheduleCheckResponse>($"{Space(spaceId)}/bots/{botId}/schedules/check", time, cancellationToken);

    public Task<ScheduleRunOnceResponse> RunBotScheduleOnce(Guid spaceId, Guid botId, Guid scheduleId, CancellationToken cancellationToken = default) =>
        Post<ScheduleRunOnceResponse>($"{Space(spaceId)}/bots/{botId}/schedules/{scheduleId}/run-once", null, cancellationToken);

    public Task<BotScheduleResponse> UpdateBotSchedule(Guid spaceId, Guid botId, Guid scheduleId, BotScheduleBody schedule, CancellationToken cancellationToken = default) =>
        Send<BotScheduleResponse>(HttpMethod.Put, $"{Space(spaceId)}/bots/{botId}/schedules/{scheduleId}", schedule, cancellationToken);

    public Task RemoveBotSchedule(Guid spaceId, Guid botId, Guid scheduleId, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"{Space(spaceId)}/bots/{botId}/schedules/{scheduleId}", cancellationToken);

    #endregion

    #region Files

    /// <summary>
    /// The bot as a file to share (admin only): its name, what it is for, its instructions, and its model as a
    /// suggestion. Nothing else of the bot is in it. <see cref="BotFiles.Write"/> turns it into the file's text.
    /// </summary>
    public Task<BotFile> ExportBot(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<BotFile>($"{Space(spaceId)}/bots/{botId}/export", cancellationToken);

    /// <summary>
    /// The bot a file would add (admin only), for a person to read before importing it. The server reads the file
    /// strictly and contacts nothing. A file it cannot read is refused with a sentence that says why.
    /// </summary>
    public Task<BotImportPreview> PreviewBotImport(Guid spaceId, string file, CancellationToken cancellationToken = default) =>
        Post<BotImportPreview>($"{Space(spaceId)}/bots/import/preview", new BotFileText(file), cancellationToken);

    /// <summary>
    /// Imports a bot file (admin only): the same text that was read for review. The bot thinks with the model the
    /// preview named and starts paused; turn it on with <see cref="ResumeBot"/>.
    /// </summary>
    public Task<BotSummary> ImportBot(Guid spaceId, string file, CancellationToken cancellationToken = default) =>
        Post<BotSummary>($"{Space(spaceId)}/bots/import", new BotFileText(file), cancellationToken);

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

    /// <summary>The space's limits on one piece of a bot's work, which the bot's own limits may only lower.</summary>
    public Task<BotRunLimitOptions> GetBotRunLimitOptions(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<BotRunLimitOptions>($"{Space(spaceId)}/bots/{botId}/limits/options", cancellationToken);

    /// <summary>The bot's own sandbox: whether it exists, its state, whether it has a screen, whether the bot is working.</summary>
    public Task<BotSandboxStatus> GetBotSandbox(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<BotSandboxStatus>($"{Space(spaceId)}/bots/{botId}/sandbox", cancellationToken);

    public Task<BotSandboxStatus> PauseBotSandbox(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Post<BotSandboxStatus>($"{Space(spaceId)}/bots/{botId}/sandbox/pause", null, cancellationToken);

    public Task<BotSandboxStatus> ResumeBotSandbox(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Post<BotSandboxStatus>($"{Space(spaceId)}/bots/{botId}/sandbox/resume", null, cancellationToken);

    /// <summary>Replaces the sandbox with a fresh one. Its files are gone.</summary>
    public Task<BotSandboxStatus> RebuildBotSandbox(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Post<BotSandboxStatus>($"{Space(spaceId)}/bots/{botId}/sandbox/rebuild", null, cancellationToken);

    /// <summary>Destroys the sandbox now. Its files are gone; the next use makes a new one.</summary>
    public Task<BotSandboxStatus> TurnOffBotSandbox(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Post<BotSandboxStatus>($"{Space(spaceId)}/bots/{botId}/sandbox/turn-off", null, cancellationToken);

    /// <summary>The sandbox's screen as PNG bytes, starting or resuming the sandbox if needed.</summary>
    public Task<byte[]> GetBotSandboxScreen(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        GetBotSandboxScreen(spaceId, botId, wake: true, cancellationToken);

    /// <summary>The sandbox's screen as PNG bytes. Pass wake=false for an automatic refresh.</summary>
    public async Task<byte[]> GetBotSandboxScreen(Guid spaceId, Guid botId, bool wake, CancellationToken cancellationToken)
    {
        var options = wake ? string.Empty : "?wake=false";
        using var response = await Send(HttpMethod.Post, $"{Space(spaceId)}/bots/{botId}/sandbox/screen{options}", null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await DroverApiException.From(response, cancellationToken);
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>One click, typed text, key press, scroll, or pointer action on the sandbox's screen.</summary>
    public Task SendBotSandboxInput(Guid spaceId, Guid botId, SandboxInputRequest input, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Post, $"{Space(spaceId)}/bots/{botId}/sandbox/input", input, cancellationToken);

    /// <summary>
    /// Holds a mouse button (left, right or middle) down at a point on the sandbox's screen. The server lets go of it
    /// when the sandbox is paused and after ten seconds with no move or release, so send a move now and then while it
    /// is held, and a release when it is not.
    /// </summary>
    public Task PressBotSandboxButton(
        Guid spaceId,
        Guid botId,
        int x,
        int y,
        string button = "left",
        CancellationToken cancellationToken = default) =>
        SendBotSandboxInput(
            spaceId,
            botId,
            new SandboxInputRequest("down", X: x, Y: y, Button: button),
            cancellationToken);

    /// <summary>Moves the pointer. A button that is held stays held, and its ten seconds start again.</summary>
    public Task MoveBotSandboxPointer(
        Guid spaceId,
        Guid botId,
        int x,
        int y,
        CancellationToken cancellationToken = default) =>
        SendBotSandboxInput(spaceId, botId, new SandboxInputRequest("move", X: x, Y: y), cancellationToken);

    /// <summary>Lets go of a mouse button, at a point when one is given and where the pointer is when not.</summary>
    public Task ReleaseBotSandboxButton(
        Guid spaceId,
        Guid botId,
        string button = "left",
        int? x = null,
        int? y = null,
        CancellationToken cancellationToken = default) =>
        SendBotSandboxInput(
            spaceId,
            botId,
            new SandboxInputRequest("up", X: x, Y: y, Button: button),
            cancellationToken);

    /// <summary>
    /// Presses at the start, moves along a short line, and lets go at the end, in one request that always lets go. For
    /// a client that cannot send a press, moves and a release as they happen.
    /// </summary>
    public Task DragBotSandboxPointer(
        Guid spaceId,
        Guid botId,
        int fromX,
        int fromY,
        int toX,
        int toY,
        string button = "left",
        CancellationToken cancellationToken = default) =>
        SendBotSandboxInput(
            spaceId,
            botId,
            new SandboxInputRequest("drag", X: fromX, Y: fromY, Button: button, X2: toX, Y2: toY),
            cancellationToken);

    public Task<SandboxCommandResponse> RunBotSandboxCommand(Guid spaceId, Guid botId, string command, CancellationToken cancellationToken = default) =>
        Post<SandboxCommandResponse>($"{Space(spaceId)}/bots/{botId}/sandbox/commands", new SandboxCommandRequest(command), cancellationToken);

    /// <summary>What a rule on this server may use.</summary>
    public Task<BotRuleOptions> GetBotRuleOptions(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<BotRuleOptions>($"{Space(spaceId)}/bots/{botId}/rules/options", cancellationToken);

    /// <summary>What the bot's rules would do with a sample. The server decides, with the evaluator a run uses.</summary>
    public Task<TryRulesResponse> TryBotRules(Guid spaceId, Guid botId, TryRulesRequest sample, CancellationToken cancellationToken = default) =>
        Post<TryRulesResponse>($"{Space(spaceId)}/bots/{botId}/rules/try", sample, cancellationToken);

    #endregion

    #region Removing

    private static string BeneathQuery(bool? removeBeneath) => removeBeneath switch
    {
        true => "?beneath=remove",
        false => "?beneath=keep",
        null => string.Empty,
    };

    #endregion
}
