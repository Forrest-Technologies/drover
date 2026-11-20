namespace Drover.Client.Streaming;

/// <summary>
/// Reads the lightweight signal stream and connects again when it drops. Signals have no replay: one missed while
/// disconnected is gone, which is why a signal only ever says "fetch again", and why this reader says
/// <see cref="UserSignalKinds.Reconnected"/> itself each time it has to connect again. While
/// <see cref="StreamPresence"/> says the app is off screen it holds no connection, so the server sees nobody watching
/// and wakes the person's devices instead; it connects again, and says so, the moment the app is back.
/// </summary>
public sealed class SignalReader(HttpClient http, string path, StreamPresence? presence = null, Action<Exception>? dropped = null)
{
    #region Private Fields

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    #endregion

    #region Public Methods

    public async IAsyncEnumerable<UserSignal> Read([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var backoff = new Backoff();
        var connection = new SseConnection(http, path, presence, dropped);

        while (true)
        {
            await foreach (var data in connection.Read(null, cancellationToken))
            {
                if (JsonSerializer.Deserialize<UserSignal>(data, json) is { } signal)
                {
                    backoff.Reset();
                    yield return signal;
                }
            }

            // The connection says why it ended, because the app may be back on screen by now.
            if (presence is not null && (connection.EndedByPause || !presence.IsShown))
            {
                await presence.WhenShown(cancellationToken);
                backoff.Reset();
            }
            else
            {
                await Task.Delay(backoff.Next(), cancellationToken);
            }

            yield return new UserSignal(UserSignalKinds.Reconnected, null, null);
        }
    }

    #endregion
}
