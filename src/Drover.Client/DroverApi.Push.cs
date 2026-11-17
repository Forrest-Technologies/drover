namespace Drover.Client;

/// <summary>Notifications: this device, the switch per bot, and what a woken device shows.</summary>
public sealed partial class DroverApi
{
    #region Push

    public async Task<PushWakeText?> ReadPushWake(string kind, Guid threadId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"api/push/wake?kind={Uri.EscapeDataString(kind)}&thread={threadId}", cancellationToken);
        return await response.ReadValueUnless<PushWakeText>(HttpStatusCode.NotFound, cancellationToken);
    }

    /// <summary>The key a device subscribes with, or why this server cannot send notifications.</summary>
    public Task<PushKeyResponse> GetPushKey(CancellationToken cancellationToken = default) =>
        Get<PushKeyResponse>("api/push/key", cancellationToken);

    public Task TurnOnPush(PushDeviceRequest device, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Put, "api/push/device", device, cancellationToken);

    public Task TestPush(string endpoint, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Post, "api/push/device/test", new PushDeviceQuery(endpoint), cancellationToken);

    public Task TurnOffPush(string endpoint, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Post, "api/push/device/off", new PushDeviceQuery(endpoint), cancellationToken);

    public Task<PushDeviceState> GetPushDeviceState(string endpoint, CancellationToken cancellationToken = default) =>
        Post<PushDeviceState>("api/push/device/state", new PushDeviceQuery(endpoint), cancellationToken);

    public Task<BotNotifySetting> GetBotNotify(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<BotNotifySetting>($"{Space(spaceId)}/bots/{botId}/notify", cancellationToken);

    public Task<BotNotifySetting> SetBotNotify(Guid spaceId, Guid botId, bool notifies, CancellationToken cancellationToken = default) =>
        Send<BotNotifySetting>(HttpMethod.Put, $"{Space(spaceId)}/bots/{botId}/notify", new BotNotifySetting(notifies), cancellationToken);

    #endregion
}
