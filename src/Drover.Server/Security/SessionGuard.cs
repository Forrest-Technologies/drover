namespace Drover.Server.Security;

/// <summary>
/// Keeps an open event stream tied to the session that opened it. A request checks its session once, when it
/// starts; a stream lives for minutes, so before each write it checks again whenever <see cref="CheckInterval"/>
/// has passed since the last check, or when <see cref="SessionRevocations"/> said a session of the person ended.
/// That announcement also starts a check at once, so a quiet stream ends promptly. When the session is gone,
/// <see cref="Ended"/> is cancelled and nothing more is written.
/// </summary>
public sealed class SessionGuard : IAsyncDisposable
{
    #region Private Fields

    private readonly Guid userId;

    private readonly Guid sessionId;

    private readonly IServiceScopeFactory scopes;

    private readonly TimeProvider time;

    private readonly ILogger logger;

    private readonly CancellationTokenSource ended = new();

    private readonly SemaphoreSlim checking = new(1, 1);

    private readonly IDisposable listening;

    private readonly Lock gate = new();

    // Ticks of the last check, read by the stream and written by a check that may run on another thread.
    private long checkedAtTicks;

    private bool disposed;

    private int heard;

    private int checkedThrough;

    private Task heardCheck = Task.CompletedTask;

    #endregion

    #region Constructors

    private SessionGuard(Guid userId, Guid sessionId, IServiceProvider services)
    {
        this.userId = userId;
        this.sessionId = sessionId;
        scopes = services.GetRequiredService<IServiceScopeFactory>();
        time = services.GetRequiredService<TimeProvider>();
        logger = services.GetRequiredService<ILogger<SessionGuard>>();
        checkedAtTicks = time.GetUtcNow().UtcTicks;
        listening = services.GetRequiredService<SessionRevocations>().Listen(userId, Heard);
    }

    #endregion

    #region Properties

    public static TimeSpan CheckInterval { get; } = TimeSpan.FromSeconds(15);

    /// <summary>Cancelled when the stream's session is found gone.</summary>
    public CancellationToken Ended => ended.Token;

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        Task pending;

        lock (gate)
        {
            disposed = true;
            pending = heardCheck;
        }

        listening.Dispose();
        await pending;
        ended.Dispose();
        checking.Dispose();
    }

    #endregion

    #region Public Methods

    /// <summary>The guard for the request's own session, which has already passed the session check.</summary>
    public static SessionGuard Start(HttpContext context) =>
        new(
            context.User.GetUserId(),
            context.User.GetSessionId() ?? Guid.Empty,
            context.RequestServices);

    /// <summary>Whether the stream may write now. Checks the session first when due or after a revocation.</summary>
    public async Task<bool> MayWrite(CancellationToken cancellationToken)
    {
        var nothingHeard = Volatile.Read(ref checkedThrough) == Volatile.Read(ref heard);

        var checkedAt = new DateTimeOffset(Volatile.Read(ref checkedAtTicks), TimeSpan.Zero);

        if (nothingHeard && time.GetUtcNow() - checkedAt < CheckInterval)
        {
            return !ended.IsCancellationRequested;
        }

        return await Check(cancellationToken);
    }

    #endregion

    #region Private Methods

    private void Heard()
    {
        Interlocked.Increment(ref heard);

        // A guard being disposed hears nothing more; each check it does start is awaited before it lets go.
        lock (gate)
        {
            if (!disposed)
            {
                heardCheck = Task.WhenAll(heardCheck, CheckAfterHearing());
            }
        }
    }

    private async Task CheckAfterHearing()
    {
        try
        {
            await Check(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The stream's own next write checks again, so a failed check here only delays the end.
            logger.LogWarning(exception, "Could not check a stream's session after a revocation");
        }
    }

    private async Task<bool> Check(CancellationToken cancellationToken)
    {
        await checking.WaitAsync(cancellationToken);

        try
        {
            if (ended.IsCancellationRequested)
            {
                return false;
            }

            var through = Volatile.Read(ref heard);
            await using var scope = scopes.CreateAsyncScope();
            var open = await scope.ServiceProvider.GetRequiredService<SessionService>()
                .IsOpen(userId, sessionId, cancellationToken);
            Volatile.Write(ref checkedAtTicks, time.GetUtcNow().UtcTicks);
            Volatile.Write(ref checkedThrough, through);

            if (!open)
            {
                await ended.CancelAsync();
            }

            return open;
        }
        finally
        {
            checking.Release();
        }
    }

    #endregion
}
