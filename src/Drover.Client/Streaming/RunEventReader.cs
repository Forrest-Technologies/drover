namespace Drover.Client.Streaming;

/// <summary>
/// Reads one run's events and keeps reading across dropped connections. It remembers the last sequence number it
/// handed out, sends it as <c>Last-Event-ID</c> when it connects again, and drops anything at or below it, so the
/// caller sees every event once, in order. The phone locking, the tab sleeping, and the server restarting all look
/// the same from here. The server closes the stream when the root run has ended and everything is sent, and this
/// reader ends with it.
/// </summary>
public sealed class RunEventReader(HttpClient http, string runPath, Guid rootRunId, Action<Exception>? dropped = null)
{
    #region Private Fields

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    #endregion

    #region Public Methods

    public async IAsyncEnumerable<RunEventResponse> Read(long afterSeq, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var last = afterSeq;
        var backoff = new Backoff();
        var connection = new SseConnection(http, $"{runPath}/events", dropped);

        while (true)
        {
            var ended = false;

            await foreach (var data in connection.Read(last.ToString(CultureInfo.InvariantCulture), cancellationToken))
            {
                if (JsonSerializer.Deserialize<RunEventResponse>(data, json) is not { } stored || stored.Seq <= last)
                {
                    continue;
                }

                last = stored.Seq;
                backoff.Reset();
                yield return stored;
                ended = stored.RunId == rootRunId && RunEventTypes.IsTerminal(stored.Type);
            }

            if (ended || await HasEndedWithNothingLeft(last, cancellationToken))
            {
                yield break;
            }

            await Task.Delay(backoff.Next(), cancellationToken);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// A stream that stops without the run's last event is a dropped connection, with one exception: a reader that
    /// had already seen the last event gets an empty stream. Asking the run tells the two apart.
    /// </summary>
    private async Task<bool> HasEndedWithNothingLeft(long last, CancellationToken cancellationToken)
    {
        var asked = await AskRun(cancellationToken);
        return asked.Value is { Status: "completed" or "failed" or "cancelled" } run && run.LastEventSeq <= last;
    }

    private async Task<Attempt<RunSummary>> AskRun(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(runPath, cancellationToken);
            return response.IsSuccessStatusCode
                ? new Attempt<RunSummary>(await response.Content.ReadFromJsonAsync<RunSummary>(json, cancellationToken), null)
                : default;
        }
        catch (HttpRequestException unreachable)
        {
            return new Attempt<RunSummary>(null, unreachable);
        }
    }

    #endregion
}
