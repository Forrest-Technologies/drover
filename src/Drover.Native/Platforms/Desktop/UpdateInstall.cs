namespace Drover.Native.Platform;

/// <summary>The operating system step of an update: runs the installer for a package that passed verification.</summary>
internal interface IUpdateInstaller
{
    /// <summary>
    /// Starts the installer for the package at <paramref name="path"/>. Calls <paramref name="closeApplication"/>
    /// only after the installer has started. Returns null when it started, and the reason when it did not.
    /// </summary>
    Task<InstallUpdateFailureReason?> Start(string path, Action closeApplication);
}


/// <summary>Hands a verified package to the operating system installer and reports the outcome.</summary>
internal static class UpdateInstall
{
    #region Public Methods

    public const string InstallerProblem = "Drover could not open the downloaded installer.";

    /// <summary>
    /// Downloads the package, verifies its signature, and starts the installer only when the host allows it.
    /// Returns the state to show. The application is closed by <paramref name="closeApplication"/> only after
    /// <paramref name="installer"/> reports that the installer started.
    /// </summary>
    public static async Task<DesktopUpdateState> Run(
        IUpdateInstaller installer,
        Func<Task<string>> download,
        string? publicKey,
        string? signature,
        UpdateHost host,
        DesktopUpdateState state,
        Action<DesktopUpdateState> progress,
        Action closeApplication)
    {
        progress(state with { Phase = DesktopUpdatePhase.Downloading, Problem = null });
        var path = await download();
        var package = await File.ReadAllBytesAsync(path);
        var decision = UpdatePackageTrust.Decide(publicKey, signature, package, host, path, state);
        if (!decision.StartInstaller)
        {
            return decision.State;
        }

        progress(decision.State);
        var failure = await installer.Start(path, closeApplication);
        return failure is null
            ? decision.State
            : decision.State with
            {
                Phase = DesktopUpdatePhase.Failed,
                Problem = failure is InstallUpdateFailureReason.InvalidSignature
                    ? UpdatePackageTrust.SignatureProblem
                    : InstallerProblem,
            };
    }

    #endregion
}


/// <summary>Runs the update library's installer, which checks the signature of the file again before it starts.</summary>
internal sealed class SparkleInstaller(SparkleUpdater updater, AppCastItem item) : IUpdateInstaller
{
    #region Interface Implementations

    public async Task<InstallUpdateFailureReason?> Start(string path, Action closeApplication)
    {
        InstallUpdateFailureReason? failure = null;
        var started = false;

        bool Failed(InstallUpdateFailureReason reason, string? failedPath)
        {
            failure = reason;
            return false;
        }

        // The library asks the application to close only after the installer process has started.
        void Close()
        {
            started = true;
            closeApplication();
        }

        updater.InstallUpdateFailed += Failed;
        updater.CloseApplication += Close;

        try
        {
            await updater.InstallUpdate(item, path);
        }
        finally
        {
            updater.InstallUpdateFailed -= Failed;
            updater.CloseApplication -= Close;
        }

        return failure ?? (started ? null : InstallUpdateFailureReason.CanceledByUserViaEvent);
    }

    #endregion
}
