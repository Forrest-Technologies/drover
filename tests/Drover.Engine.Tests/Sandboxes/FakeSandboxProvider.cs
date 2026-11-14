namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// A sandbox provider in memory, standing for the outside world: it keeps each sandbox's state, files, metadata and
/// token, refuses a handle whose space or group differ from its record and a token that is not the current one, as a
/// real provider must, and records every call. A command prints its own command line, unless the test says otherwise.
/// Every token it issues is new random text, made for this test run.
/// </summary>
public sealed class FakeSandboxProvider : ISandboxProvider
{
    #region Private Fields

    private readonly ConcurrentDictionary<string, FakeSandbox> sandboxes = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<Guid, string> byIdempotencyKey = new();

    private readonly Lock creating = new();

    private int created;

    #endregion

    #region Properties

    public string Kind => "fake";

    /// <summary>How many sandboxes were really made. A repeated idempotency key makes none.</summary>
    public int Created => created;

    public ConcurrentQueue<SandboxSpec> Specs { get; } = new();

    public ConcurrentQueue<CommandRequest> Commands { get; } = new();

    public ConcurrentQueue<NetworkPolicy> NetworksSet { get; } = new();

    /// <summary>Every handle a pause or a destroy was asked with, in order.</summary>
    public ConcurrentQueue<SandboxHandle> Pauses { get; } = new();

    public ConcurrentQueue<SandboxHandle> Destroys { get; } = new();

    /// <summary>The provider ids whose destruction fails, as when the provider is down.</summary>
    public ConcurrentDictionary<string, bool> FailingDestroys { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, resuming issues a new token, as a provider may.</summary>
    public bool NewTokenOnResume { get; set; }

    /// <summary>When set, every check of a handle finds another group's metadata.</summary>
    public bool MetadataMismatch { get; set; }

    /// <summary>How a command runs. The default prints the command line and exits 0.</summary>
    public Func<CommandRequest, string, CancellationToken, IAsyncEnumerable<CommandOutput>> Behaviour { get; set; } = PrintCommand;

    public IReadOnlyCollection<string> Tokens => [.. sandboxes.Values.Select(sandbox => sandbox.Token)];

    #endregion

    #region Public Methods

    public FakeSandbox Only() => sandboxes.Values.Single();

    /// <summary>The sandbox whose current token this is, as a command's behaviour is handed it.</summary>
    public FakeSandbox WithToken(string token) => sandboxes.Values.Single(sandbox => sandbox.Token == token);

    public void PauseAll()
    {
        foreach (var sandbox in sandboxes.Values)
        {
            sandbox.State = ProviderSandboxState.Paused;
        }
    }

    public void LoseAll()
    {
        foreach (var sandbox in sandboxes.Values)
        {
            sandbox.State = ProviderSandboxState.Gone;
        }
    }

    #endregion

    #region Interface Implementations

    public Task<SandboxHandle> Create(SandboxSpec spec, CancellationToken cancellationToken)
    {
        Specs.Enqueue(spec);

        lock (creating)
        {
            if (!byIdempotencyKey.TryGetValue(spec.IdempotencyKey, out var id))
            {
                id = $"fake{Interlocked.Increment(ref created)}{Guid.NewGuid():N}"[..20];
                sandboxes[id] = new FakeSandbox(id, spec.SpaceId, spec.SharingGroupId, NewToken());
                byIdempotencyKey[spec.IdempotencyKey] = id;
            }

            var sandbox = sandboxes[id];
            return Task.FromResult(new SandboxHandle(id, sandbox.SpaceId, sandbox.GroupId, sandbox.Token));
        }
    }

    public Task<SandboxInfo> Describe(SandboxHandle sandbox, CancellationToken cancellationToken) =>
        Task.FromResult(sandboxes.TryGetValue(sandbox.ProviderSandboxId, out var found) && found.State is not ProviderSandboxState.Gone
            ? new SandboxInfo(found.State, new Dictionary<string, string>(), null)
            : new SandboxInfo(ProviderSandboxState.Gone, new Dictionary<string, string>(), null));

    public Task KeepAlive(SandboxHandle sandbox, TimeSpan idle, CancellationToken cancellationToken)
    {
        Check(sandbox).KeptFor = idle;
        return Task.CompletedTask;
    }

    public Task Pause(SandboxHandle sandbox, CancellationToken cancellationToken)
    {
        Pauses.Enqueue(sandbox);
        Check(sandbox).State = ProviderSandboxState.Paused;
        return Task.CompletedTask;
    }

    public Task<SandboxHandle> Resume(SandboxHandle sandbox, TimeSpan idle, CancellationToken cancellationToken)
    {
        var found = Check(sandbox);

        if (found.State is ProviderSandboxState.Paused && NewTokenOnResume)
        {
            found.Token = NewToken();
        }

        found.State = ProviderSandboxState.Running;
        found.KeptFor = idle;
        return Task.FromResult(sandbox with { AccessToken = found.Token });
    }

    public Task Destroy(SandboxHandle sandbox, CancellationToken cancellationToken)
    {
        Destroys.Enqueue(sandbox);

        if (FailingDestroys.ContainsKey(sandbox.ProviderSandboxId))
        {
            throw new SandboxProviderException(SandboxFailure.Unavailable, "The provider is not available.");
        }

        if (sandboxes.TryGetValue(sandbox.ProviderSandboxId, out var found))
        {
            found.State = ProviderSandboxState.Gone;
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<CommandOutput> Run(SandboxHandle sandbox, CommandRequest command, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var found = Enter(sandbox);
        Commands.Enqueue(command);

        await foreach (var output in Behaviour(command, found.Token, cancellationToken))
        {
            yield return output;
        }
    }

    public Task<Stream> ReadFile(SandboxHandle sandbox, string path, CancellationToken cancellationToken) =>
        Enter(sandbox).Files.TryGetValue(path, out var content)
            ? Task.FromResult<Stream>(new MemoryStream(content, writable: false))
            : throw new SandboxProviderException(SandboxFailure.NotFound, "Not found.");

    public async Task WriteFile(SandboxHandle sandbox, string path, Stream content, CancellationToken cancellationToken)
    {
        var found = Enter(sandbox);
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        found.Files[path] = copy.ToArray();
    }

    public Task<IReadOnlyList<SandboxEntry>> ListFiles(SandboxHandle sandbox, string path, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SandboxEntry>>(
        [
            .. Enter(sandbox).Files
                .Where(file => file.Key.StartsWith($"{path.TrimEnd('/')}/", StringComparison.Ordinal))
                .Select(file => new SandboxEntry(file.Key[(path.TrimEnd('/').Length + 1)..], file.Key, SandboxEntryKind.File, file.Value.Length, null)),
        ]);

    public Task SetNetworkPolicy(SandboxHandle sandbox, NetworkPolicy policy, CancellationToken cancellationToken)
    {
        Check(sandbox);
        NetworksSet.Enqueue(policy);
        return Task.CompletedTask;
    }

    #endregion

    #region Private Methods

    /// <summary>The provider's record, checked against the handle as a real adapter checks its metadata.</summary>
    private FakeSandbox Check(SandboxHandle sandbox)
    {
        if (!sandboxes.TryGetValue(sandbox.ProviderSandboxId, out var found) || found.State is ProviderSandboxState.Gone)
        {
            throw new SandboxProviderException(SandboxFailure.NotFound, "The sandbox does not exist.");
        }

        if (MetadataMismatch || found.SpaceId != sandbox.SpaceId || found.GroupId != sandbox.SharingGroupId)
        {
            throw new SandboxProviderException(SandboxFailure.HandleMismatch, "The sandbox belongs to another group.");
        }

        return found;
    }

    /// <summary>A running sandbox that accepts the handle's token. A paused one is refused, as the real adapter does.</summary>
    private FakeSandbox Enter(SandboxHandle sandbox)
    {
        var found = Check(sandbox);

        if (found.State is not ProviderSandboxState.Running)
        {
            throw new SandboxProviderException(SandboxFailure.Rejected, "The sandbox is paused.");
        }

        return found.Token == sandbox.AccessToken
            ? found
            : throw new SandboxProviderException(SandboxFailure.Unauthorized, "The sandbox refused the token.");
    }

    private static string NewToken() => $"tok_{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}";

    private static async IAsyncEnumerable<CommandOutput> PrintCommand(CommandRequest command, string token, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return new CommandStarted(1);
        yield return new CommandStdout(Encoding.UTF8.GetBytes($"{command.Arguments[^1]}\n"));
        yield return new CommandExited(0, null);
    }

    #endregion

    #region Nested Types

    public sealed class FakeSandbox(string id, Guid spaceId, Guid groupId, string token)
    {
        #region Properties

        public string Id => id;

        public Guid SpaceId => spaceId;

        public Guid GroupId => groupId;

        public string Token { get; set; } = token;

        public ProviderSandboxState State { get; set; } = ProviderSandboxState.Running;

        public TimeSpan KeptFor { get; set; }

        public ConcurrentDictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

        #endregion
    }

    #endregion
}
