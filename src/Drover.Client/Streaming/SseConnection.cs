namespace Drover.Client.Streaming;

/// <summary>What one try at the network came to: a value, or the failure that means "connect again".</summary>
public readonly record struct Attempt<T>(T? Value, Exception? Failure);

/// <summary>
/// One Server-Sent Events connection. It yields the data of each frame until the connection ends or drops, and then
/// stops; the caller decides whether to connect again. A refusal (not found, not allowed) is thrown instead, because
/// connecting again would not change it. <paramref name="dropped"/> hears why a connection was lost, so a screen can
/// say it is reconnecting.
/// </summary>
public sealed class SseConnection(HttpClient http, string path, Action<Exception>? dropped = null)
{
    #region Public Methods

    public async IAsyncEnumerable<string> Read(string? lastEventId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var connected = await Connect(lastEventId, cancellationToken);

        if (connected.Value is not { } response)
        {
            Report(connected.Failure);
            yield break;
        }

        using (response)
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var frames = SseParser.Create(body).EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                var moved = await MoveNext(frames);

                if (!moved.Value)
                {
                    Report(moved.Failure);
                    yield break;
                }

                yield return frames.Current.Data;
            }
        }
    }

    #endregion

    #region Private Methods

    private async Task<Attempt<HttpResponseMessage>> Connect(string? lastEventId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.ParseAdd("text/event-stream");

        if (lastEventId is not null)
        {
            request.Headers.Add("Last-Event-ID", lastEventId);
        }

        try
        {
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new Attempt<HttpResponseMessage>(response, null);
            }

            using (response)
            {
                var refusal = await DroverApiException.From(response, cancellationToken);

                // A server that is starting or overloaded answers 5xx; that passes. Anything else will not.
                return (int)response.StatusCode >= 500 ? new Attempt<HttpResponseMessage>(null, refusal) : throw refusal;
            }
        }
        catch (HttpRequestException unreachable)
        {
            return new Attempt<HttpResponseMessage>(null, unreachable);
        }
    }

    private static async Task<Attempt<bool>> MoveNext(IAsyncEnumerator<SseItem<string>> frames)
    {
        try
        {
            return new Attempt<bool>(await frames.MoveNextAsync(), null);
        }
        catch (Exception lost) when (lost is IOException or HttpRequestException)
        {
            return new Attempt<bool>(false, lost);
        }
    }

    private void Report(Exception? failure)
    {
        if (failure is not null)
        {
            dropped?.Invoke(failure);
        }
    }

    #endregion
}
