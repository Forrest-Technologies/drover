namespace Drover.Engine.Push;

/// <summary>
/// Wakes a person's devices when a run of theirs finishes, fails, or waits on them, and they have no live view of the
/// space open. The decision to wake is taken when the run ends; the sending happens off the run's path. Each tenant
/// transaction here is short: one to read the person's devices and choice, none while a push service is asked, one to
/// forget the devices the push service says are gone. A push the service could not take is tried again later.
/// </summary>
public sealed class PushNotifier(
    IEnumerable<IPushChannel> channels,
    UserSignalHub signals,
    RunScopes scopes,
    IOptions<RunOptions> options,
    TimeProvider time,
    ILogger<PushNotifier> logger) : IDisposable
{
    #region Private Fields

    private readonly IPushChannel? channel = channels.FirstOrDefault();

    private readonly CancellationTokenSource stopping = new();

    #endregion

    #region Public Methods

    /// <summary>The wake-up a root run's end calls for, or null when it calls for none (a cancelled run, a running one).</summary>
    public static string? KindFor(RunStatus status) => status switch
    {
        RunStatus.Completed => PushKinds.RunFinished,
        RunStatus.Failed => PushKinds.RunFailed,
        RunStatus.Waiting => PushKinds.ApprovalWaiting,
        _ => null,
    };

    /// <summary>
    /// Wakes the person's devices about a root run that ended as <paramref name="status"/>, unless the person is
    /// watching the space right now. Returns at once; the push is sent in the background.
    /// </summary>
    public void RunEnded(Guid spaceId, Guid userId, Guid botId, Guid threadId, RunStatus status)
    {
        if (channel is null || KindFor(status) is not { } kind || signals.IsWatched(spaceId, userId))
        {
            return;
        }

        var job = new PushJob(spaceId, userId, botId, new PushWakeUp(kind, threadId), null, 0);

        // A run can end inside its own tenant transaction. The push must not inherit it, so the context does not flow.
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(() => Send(job));
        }
    }

    /// <summary>
    /// One try at a job: each device the job is for is asked once. Returns the job for the devices whose push service
    /// asked to be tried again, or null when there is nothing more to do.
    /// </summary>
    public async Task<PushJob?> Deliver(PushJob job, CancellationToken cancellationToken)
    {
        if (channel is null)
        {
            return null;
        }

        var tenant = scopes.For(job.SpaceId, job.UserId);
        var devices = await tenant.Transact(services => DevicesFor(services, job, cancellationToken), cancellationToken);
        List<Guid> gone = [];
        List<Guid> later = [];

        foreach (var device in devices)
        {
            var outcome = await SendTo(channel, device, job.WakeUp, cancellationToken);
            (outcome switch
            {
                PushOutcome.SubscriptionGone => gone,
                PushOutcome.RetryLater => later,
                _ => null,
            })?.Add(device.Id);
        }

        if (gone.Count > 0)
        {
            await tenant.Transact(
                services => services.GetRequiredService<PushRepository>().RemoveSubscriptions(gone, cancellationToken),
                cancellationToken);
            logger.LogInformation("Forgot {Count} devices their push service reports gone", gone.Count);
        }

        return later.Count > 0 && job.Attempt < options.Value.PushRetryDelays.Count
            ? job with { DeviceIds = later, Attempt = job.Attempt + 1 }
            : null;
    }

    #endregion

    #region Interface Implementations

    public void Dispose()
    {
        stopping.Cancel();
        stopping.Dispose();
    }

    #endregion

    #region Private Methods

    /// <summary>One device's try. A failure of one device is that device's alone: the others are still sent to.</summary>
    private async Task<PushOutcome> SendTo(IPushChannel to, PushSubscription device, PushWakeUp wakeUp, CancellationToken cancellationToken)
    {
        try
        {
            return await to.Send(device, wakeUp, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "A push to device {SubscriptionId} failed; it will be tried again", device.Id);
            return PushOutcome.RetryLater;
        }
    }

    /// <summary>Tries the job, and again after each delay while a push service asks for it, until the server stops.</summary>
    private async Task Send(PushJob job)
    {
        try
        {
            // Read inside the try: a notifier disposed while the server stops has no token to give.
            var token = stopping.Token;
            var next = await Deliver(job, token);

            while (next is not null)
            {
                await Task.Delay(options.Value.PushRetryDelays[next.Attempt - 1], time, token);
                next = await Deliver(next, token);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            logger.LogDebug(exception, "A push for thread {ThreadId} was not sent: the server is stopping", job.WakeUp.ThreadId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "A push for thread {ThreadId} could not be sent", job.WakeUp.ThreadId);
        }
    }

    /// <summary>The person's devices this job is for, or none when the person has asked the bot not to notify them.</summary>
    private static async Task<IReadOnlyList<PushSubscription>> DevicesFor(IServiceProvider services, PushJob job, CancellationToken cancellationToken)
    {
        var push = services.GetRequiredService<PushRepository>();

        if (await push.IsMuted(job.BotId, job.UserId, cancellationToken))
        {
            return [];
        }

        var devices = await push.ListSubscriptions(cancellationToken);
        return job.DeviceIds is { } only ? [.. devices.Where(device => only.Contains(device.Id))] : devices;
    }

    #endregion
}


/// <summary>
/// One wake-up for one person's devices. <see cref="DeviceIds"/> is null on the first try, which is for every device
/// of theirs, and names the devices still owed it on a later try.
/// </summary>
public sealed record PushJob(Guid SpaceId, Guid UserId, Guid BotId, PushWakeUp WakeUp, IReadOnlyList<Guid>? DeviceIds, int Attempt);
