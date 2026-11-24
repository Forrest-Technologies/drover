namespace Drover.Native.Platform;

/// <summary>
/// Runs update checks and the checks setting for one installation. Every check and every change of the setting takes
/// a new generation. A check that resumes after a newer generation began does nothing more: it does not replace the
/// updater, clear old downloads, or change what is shown, so it can never disturb a newer check, download, or install.
/// Disposing ends the flow for good: checks in progress are superseded, and no new check or setting change starts.
/// </summary>
internal sealed class UpdateCheckFlow(
    IDeviceSettings settings,
    string channel,
    Func<Task<string>> findFeed,
    Func<string, SparkleUpdater> buildUpdater,
    Action removeOldAttempts,
    Func<DesktopUpdateState> state,
    Action<DesktopUpdateState> change,
    ILogger logger) : IDisposable
{
    #region Private Fields

    private long generation;

    private int disposed;

    #endregion

    #region Properties

    /// <summary>The updater the latest completed check built, which an install uses.</summary>
    public SparkleUpdater? Updater { get; private set; }

    /// <summary>The update the latest completed check offered, if any.</summary>
    public AppCastItem? Available { get; private set; }

    private bool IsDisposed => Volatile.Read(ref disposed) == 1;

    #endregion

    #region Public Methods

    public void SetChecks(bool enabled)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            DesktopUpdateChecks.Store(settings, enabled);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The update preference could not be saved");
            change(UpdatePackageTrust.AfterSettingNotSaved(state()));
            return;
        }

        Interlocked.Increment(ref generation);
        change(UpdatePackageTrust.AfterChecksSwitch(state(), enabled));
        if (enabled)
        {
            _ = Check(reportFailure: false);
        }
    }

    public async Task Check(bool reportFailure)
    {
        var current = state();
        if (IsDisposed
            || current is not { Supported: true, ChecksEnabled: true }
            || current.Phase is DesktopUpdatePhase.Checking or DesktopUpdatePhase.Downloading
                or DesktopUpdatePhase.Installing)
        {
            return;
        }

        var ticket = Interlocked.Increment(ref generation);
        change(current with { Phase = DesktopUpdatePhase.Checking, Problem = null });

        try
        {
            var feed = await findFeed();
            if (IsSuperseded(ticket))
            {
                return;
            }

            Updater?.Dispose();
            Available = null;
            removeOldAttempts();
            var updater = buildUpdater(feed);
            Updater = updater;
            var result = await updater.CheckForUpdatesQuietly(ignoreSkippedVersions: true);
            if (IsSuperseded(ticket))
            {
                return;
            }

            Offer(result, reportFailure);
        }
        catch (Exception exception) when (!IsSuperseded(ticket))
        {
            logger.LogWarning(exception, "The signed desktop release feed could not be checked");
            change(state() with
            {
                Phase = reportFailure ? DesktopUpdatePhase.Failed : DesktopUpdatePhase.Idle,
                Problem = reportFailure ? UpdatePackageTrust.ProblemFor(exception) : null,
            });
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "A superseded update check failed after a newer one began");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        Interlocked.Increment(ref generation);
        Updater?.Dispose();
        Updater = null;
        Available = null;
    }

    #endregion

    #region Private Methods

    private bool IsSuperseded(long ticket) => IsDisposed || Interlocked.Read(ref generation) != ticket;

    private void Offer(UpdateInfo result, bool reportFailure)
    {
        var offered = UpdatePackageTrust.Offer(result, channel);
        var verdict = UpdatePackageTrust.AcceptFeed(settings, channel, offered?.Version);
        if (verdict.NotSaved is not null)
        {
            logger.LogWarning(verdict.NotSaved, "The newest verified update feed version could not be saved");
        }

        if (!verdict.Accepted)
        {
            change(verdict.Recorded
                ? UpdatePackageTrust.AfterStaleFeed(state(), reportFailure)
                : UpdatePackageTrust.AfterUnrecordedFeed(state(), reportFailure));
            return;
        }

        Available = offered;
        change(UpdatePackageTrust.AfterCheck(
            state(),
            result.Status,
            offered?.Version,
            reportFailure,
            () => logger.LogWarning("The signed desktop release feed could not be verified")));
    }

    #endregion
}
