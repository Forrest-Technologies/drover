namespace Drover.Adapters.E2B;

/// <summary>
/// Reads one <c>process.StartResponse</c> message: <c>{"event":{"start"|"data"|"end"|"keepalive":{...}}}</c>.
/// Proto3 JSON leaves zero values out, so a missing exit code is 0.
/// </summary>
internal static class ProcessEvents
{
    #region Public Methods

    /// <summary>The output the message carries, or null for a keep-alive or an event Drover does not use.</summary>
    public static CommandOutput? Read(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("event", out var @event)
            || @event.ValueKind != JsonValueKind.Object)
        {
            throw FailureMapping.Malformed();
        }

        if (@event.TryGetProperty("start", out var start))
        {
            return new CommandStarted(ReadInt(start, "pid"));
        }

        if (@event.TryGetProperty("data", out var data))
        {
            return ReadData(data);
        }

        if (@event.TryGetProperty("end", out var end))
        {
            return new CommandExited(ReadInt(end, "exitCode"), ReadString(end, "error"));
        }

        return null;
    }

    #endregion

    #region Private Methods

    private static CommandOutput? ReadData(JsonElement data) =>
        ReadBytes(data, "stdout") is { } stdout
            ? new CommandStdout(stdout)
            : ReadBytes(data, "stderr") is { } stderr
                ? new CommandStderr(stderr)
                : ReadBytes(data, "pty") is { } pty
                    ? new CommandStdout(pty)
                    : null;

    private static int ReadInt(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                ? number
                : throw FailureMapping.Malformed()
            : 0;

    private static string? ReadString(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static byte[]? ReadBytes(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String && value.TryGetBytesFromBase64(out var bytes)
                ? bytes
                : throw FailureMapping.Malformed()
            : null;

    #endregion
}
