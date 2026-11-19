namespace Drover.Native.Platform;

/// <summary>
/// Follows the signed-in person's live signals while the desktop app runs. The server supplies lock-screen-safe
/// words and the person's per-bot choice before anything is handed to the operating system.
/// </summary>
internal sealed class DesktopNotifications(
    DroverApi api,
    SessionState session,
    Navigator navigator,
    DesktopNotificationSink sink,
    ILogger<DesktopNotifications> logger) : ILiveNotifications, ILiveNotificationStatus, IDisposable
{
    #region Private Fields

    private CancellationTokenSource? listening;

    private long generation;

    private bool started;

    #endregion

    #region Interface Implementations

    public string StatusSentence => sink.IsAvailable
        ? "This app can show notifications while it is running. System settings can still silence them."
        : "This app follows notification events while it is running, but this desktop cannot show them.";

    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        sink.Selected += Open;
        session.Changed += SessionChanged;
        SessionChanged();
    }

    public void Dispose()
    {
        session.Changed -= SessionChanged;
        sink.Selected -= Open;
        var version = Interlocked.Increment(ref generation);
        sink.Clear(version);
        Stop();
    }

    #endregion

    #region Private Methods

    private async Task Follow(Guid spaceId, long version, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var signal in api.WatchSignals(spaceId, cancellationToken: cancellationToken))
            {
                await Notify(spaceId, version, signal, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Stopped following signals for desktop notifications");
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "The desktop notification signal stream stopped");
        }
    }

    private async Task Notify(Guid spaceId, long version, UserSignal signal, CancellationToken cancellationToken)
    {
        if (LiveNotificationDecision.Kind(signal) is not { } kind || signal.ThreadId is not { } threadId)
        {
            return;
        }

        try
        {
            var wake = await api.ReadPushWake(kind, threadId, cancellationToken);
            if (!IsCurrent(spaceId, version)
                || wake is null
                || wake.SpaceId is { } wakeSpaceId && wakeSpaceId != spaceId)
            {
                return;
            }

            var choice = await api.GetBotNotify(spaceId, wake.BotId, cancellationToken);
            if (IsCurrent(spaceId, version)
                && LiveNotificationDecision.ShouldShow(
                    wake,
                    choice.Notifies,
                    navigator.Current,
                    ((App)Application.Current).IsActive))
            {
                sink.Show(wake.Body, LiveNotificationDecision.Address(wake), version);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Stopped preparing a desktop notification for thread {ThreadId}", threadId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(
                exception,
                "A desktop notification for thread {ThreadId} could not be prepared",
                threadId);
        }
    }

    private void SessionChanged()
    {
        var version = Interlocked.Increment(ref generation);
        Stop();
        sink.Clear(version);
        if (session.Space is { } space)
        {
            listening = new();
            _ = Follow(space.Id, version, listening.Token);
        }
    }

    private bool IsCurrent(Guid spaceId, long version) =>
        version == Interlocked.Read(ref generation) && session.Space?.Id == spaceId;

    private void Open(string address, long version)
    {
        if (version != Interlocked.Read(ref generation) || session.Space is null)
        {
            return;
        }

        var app = (App)Application.Current;
        app.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            if (version == Interlocked.Read(ref generation) && session.Space is not null)
            {
                PlatformShell.Incoming.Open(address);
                app.MainWindow.Activate();
            }
        });
    }

    private void Stop()
    {
        listening?.Cancel();
        listening?.Dispose();
        listening = null;
    }

    #endregion
}
