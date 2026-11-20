namespace Drover.Client.Streaming;

/// <summary>What one try at the network came to: a value, or the failure that means "connect again".</summary>
public readonly record struct Attempt<T>(T? Value, Exception? Failure);

/// <summary>
/// One Server-Sent Events connection. It yields the data of each frame until the connection ends or drops, and then
/// stops; the caller decides whether to connect again. A refusal (not found, not allowed) is thrown instead, because
/// connecting again would not change it. <paramref name="dropped"/> hears why a connection was lost, so a screen can
/// say it is reconnecting, and <paramref name="connected"/> hears each time it is through again, so the screen can stop
/// saying so. The connection is also closed, quietly, when <paramref name="presence"/> says the app has left the screen.
/// </summary>
public sealed class SseConnection(
    HttpClient http,
    string path,
    StreamPresence? presence = null,
    Action<Exception>? dropped = null,
    Action? connected = null)
{
    #region Public Methods

    public async IAsyncEnumerable<string> Read(string? lastEventId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, presence?.Hidden ?? CancellationToken.None);
        var token = reading.Token;
        var opened = await Connect(lastEventId, cancellationToken, token);

        if (opened.Value is not { } response)
        {
            Report(opened.Failure);
            yield break;
        }

        using (response)
        {
            var opening = await Body(response, cancellationToken, token);

            if (opening.Value is not { } body)
            {
                Report(opening.Failure);
                yield break;
            }

            connected?.Invoke();
            await using var stream = body;
            await using var frames = SseParser.Create(stream).EnumerateAsync(token).GetAsyncEnumerator(token);

            while (true)
            {
                var moved = await MoveNext(frames, cancellationToken, token);

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

    // The app leaving the screen cancels the read but not the caller: that ends the connection with no failure to report.
    private static bool Paused(CancellationToken caller, CancellationToken reading) =>
        reading.IsCancellationRequested && !caller.IsCancellationRequested;

    private async Task<Attempt<HttpResponseMessage>> Connect(string? lastEventId, CancellationToken caller, CancellationToken cancellationToken)
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
        catch (OperationCanceledException paused) when (Paused(caller, cancellationToken))
        {
            return new Attempt<HttpResponseMessage>(null, paused);
        }
    }

    private static async Task<Attempt<Stream>> Body(HttpResponseMessage response, CancellationToken caller, CancellationToken cancellationToken)
    {
        try
        {
            return new Attempt<Stream>(await response.Content.ReadAsStreamAsync(cancellationToken), null);
        }
        catch (OperationCanceledException paused) when (Paused(caller, cancellationToken))
        {
            return new Attempt<Stream>(null, paused);
        }
    }

    private static async Task<Attempt<bool>> MoveNext(IAsyncEnumerator<SseItem<string>> frames, CancellationToken caller, CancellationToken cancellationToken)
    {
        try
        {
            return new Attempt<bool>(await frames.MoveNextAsync(), null);
        }
        catch (OperationCanceledException paused) when (Paused(caller, cancellationToken))
        {
            return new Attempt<bool>(false, paused);
        }
        catch (Exception lost) when (lost is IOException or HttpRequestException)
        {
            return new Attempt<bool>(false, lost);
        }
    }

    // A pause (the app leaving the screen) is the one failure that is not a drop, so it is not reported as one.
    private void Report(Exception? failure)
    {
        if (failure is not (null or OperationCanceledException))
        {
            dropped?.Invoke(failure);
        }
    }

    #endregion
}
