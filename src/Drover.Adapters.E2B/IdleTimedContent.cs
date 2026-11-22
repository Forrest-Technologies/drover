namespace Drover.Adapters.E2B;

/// <summary>
/// An upload body with its own clock. Bytes must keep moving, the daemon must answer after the last byte, and the
/// whole transfer has a ceiling. <see cref="Deadline"/> carries the caller's cancellation and whichever limit runs
/// out first, so the caller's own cancellation stays cancellation and a limit becomes a provider failure. The source
/// stream belongs to the caller and is left open.
/// </summary>
internal sealed class IdleTimedContent : HttpContent
{
    #region Private Fields

    private const int ChunkSize = 81_920;

    private readonly Stream source;

    private readonly TimeSpan idleTimeout;

    private readonly TimeSpan answerTimeout;

    private readonly CancellationTokenSource idle;

    private readonly CancellationTokenSource answer;

    private readonly CancellationTokenSource total;

    private readonly CancellationTokenSource deadline;

    #endregion

    #region Constructors

    public IdleTimedContent(
        Stream source,
        TimeProvider time,
        TimeSpan idleTimeout,
        TimeSpan answerTimeout,
        TimeSpan ceiling,
        CancellationToken caller)
    {
        this.source = source;
        this.idleTimeout = idleTimeout;
        this.answerTimeout = answerTimeout;
        idle = new(idleTimeout, time);
        answer = new(Timeout.InfiniteTimeSpan, time);
        total = new(ceiling, time);
        deadline = CancellationTokenSource.CreateLinkedTokenSource(caller, idle.Token, answer.Token, total.Token);
    }

    #endregion

    #region Properties

    public CancellationToken Deadline => deadline.Token;

    #endregion

    #region Interface Implementations

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, deadline.Token);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        using var sending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var buffer = new byte[ChunkSize];
        int read;

        while ((read = await source.ReadAsync(buffer, sending.Token)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, read), sending.Token);
            idle.CancelAfter(idleTimeout);
        }

        // The body is done, so inactivity no longer applies. Only the answer limit and the ceiling remain.
        idle.Dispose();
        answer.CancelAfter(answerTimeout);
    }

    protected override bool TryComputeLength(out long length)
    {
        if (source.CanSeek)
        {
            length = source.Length - source.Position;
            return true;
        }

        length = 0;
        return false;
    }

    #endregion

    #region IDisposable

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            deadline.Dispose();
            idle.Dispose();
            answer.Dispose();
            total.Dispose();
        }

        base.Dispose(disposing);
    }

    #endregion
}
