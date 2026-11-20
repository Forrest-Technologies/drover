namespace Drover.Contracts.Push;

/// <summary>
/// The server's public key for push, base64url, which a device subscribes with. Null when this server cannot send
/// pushes; <see cref="Problem"/> then says why in a sentence.
/// </summary>
public sealed record PushKeyResponse(string? PublicKey, string? Problem = null);


/// <summary>
/// Input only: a device's push subscription as the browser gives it, the push service's address and the device's
/// keys, base64url. Never part of a response, and it prints nothing of itself when logged.
/// </summary>
public sealed record PushDeviceRequest(string Endpoint, string PublicKey, string AuthSecret)
{
    public override string ToString() => nameof(PushDeviceRequest);
}


/// <summary>Names a device by its push service address, to ask whether it is on or to turn it off.</summary>
public sealed record PushDeviceQuery(string Endpoint);


/// <summary>
/// Input only: a device's new subscription, to take the place of the one it had at <see cref="PreviousEndpoint"/>.
/// The server does it only for the person who had the previous one. Prints nothing of itself when logged.
/// </summary>
public sealed record MovePushDeviceRequest(string PreviousEndpoint, PushDeviceRequest Device)
{
    public override string ToString() => nameof(MovePushDeviceRequest);
}


/// <summary>Whether the server will wake this device for the signed-in person.</summary>
public sealed record PushDeviceState(bool On);


/// <summary>Whether a bot may notify the signed-in person. On unless they turned it off.</summary>
public sealed record BotNotifySetting(bool Notifies);


/// <summary>
/// What a woken device shows, fetched by the device from the person's own server: a sentence that names the bot and
/// says what happened, and the conversation to open. <see cref="IsMain"/> is false for a side chat.
/// </summary>
public sealed record PushWakeText(string Body, Guid BotId, Guid ThreadId, bool IsMain, Guid? SpaceId = null);
