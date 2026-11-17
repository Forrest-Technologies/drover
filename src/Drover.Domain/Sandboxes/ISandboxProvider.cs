namespace Drover.Domain.Sandboxes;

/// <summary>
/// An isolated computer a bot can work in, at one provider. Only the sandbox broker calls this. Every operation on an
/// existing sandbox first checks the provider's own record of the sandbox against the handle, and refuses with
/// <see cref="SandboxFailure.HandleMismatch"/> when the space or sharing group differ.
/// Failures are <see cref="SandboxProviderException"/>; no provider type leaves the adapter.
/// </summary>
public interface ISandboxProvider
{
    string Kind { get; }

    Task<SandboxHandle> Create(SandboxSpec spec, CancellationToken cancellationToken);

    /// <summary>The provider's record of the sandbox. One the provider no longer has is <see cref="ProviderSandboxState.Gone"/>.</summary>
    Task<SandboxInfo> Describe(SandboxHandle sandbox, CancellationToken cancellationToken);

    /// <summary>Keeps a running sandbox for <paramref name="idle"/> from now. After that the provider pauses it.</summary>
    Task KeepAlive(SandboxHandle sandbox, TimeSpan idle, CancellationToken cancellationToken);

    /// <summary>Pauses the sandbox, keeping its files. Pausing a paused sandbox is not an error.</summary>
    Task Pause(SandboxHandle sandbox, CancellationToken cancellationToken);

    /// <summary>
    /// Resumes a paused sandbox, or keeps a running one, for <paramref name="idle"/> from now. The provider may issue a
    /// new access token, so the returned handle replaces the one passed in.
    /// </summary>
    // CA1716 flags "Resume" because it is a Visual Basic keyword. Drover is C# only, and Resume is the operation's
    // name in the design (docs/architecture/07-sandbox.md) and at every provider.
#pragma warning disable CA1716
    Task<SandboxHandle> Resume(SandboxHandle sandbox, TimeSpan idle, CancellationToken cancellationToken);
#pragma warning restore CA1716

    /// <summary>Destroys the sandbox. A sandbox that is already gone is not an error.</summary>
    Task Destroy(SandboxHandle sandbox, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a program and streams its output as it arrives. Stopping early, by cancellation or by not reading to the
    /// end, kills the process. A command still running at <see cref="CommandRequest.Timeout"/> is killed and fails with
    /// <see cref="SandboxFailure.TimedOut"/>.
    /// </summary>
    IAsyncEnumerable<CommandOutput> Run(
        SandboxHandle sandbox,
        CommandRequest command,
        CancellationToken cancellationToken);

    /// <summary>The file's content. The caller disposes the stream and limits how much it reads.</summary>
    Task<Stream> ReadFile(SandboxHandle sandbox, string path, CancellationToken cancellationToken);

    /// <summary>Writes the file, creating parent folders and replacing a file already there.</summary>
    Task WriteFile(SandboxHandle sandbox, string path, Stream content, CancellationToken cancellationToken);

    /// <summary>The entries directly inside a folder.</summary>
    Task<IReadOnlyList<SandboxEntry>> ListFiles(
        SandboxHandle sandbox,
        string path,
        CancellationToken cancellationToken);

    /// <summary>Replaces the sandbox's outbound network rules.</summary>
    Task SetNetworkPolicy(SandboxHandle sandbox, NetworkPolicy policy, CancellationToken cancellationToken);
}
