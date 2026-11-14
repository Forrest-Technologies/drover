namespace Drover.Server.Tests.Support;

/// <summary>
/// A sandbox provider in memory, for API tests: it keeps each sandbox's state, files and token, behaves like a
/// desktop (scrot writes a small PNG to the path it is given, rm removes it), prints a bash command line back, and
/// records every command. Ids and tokens are random text made for the test run.
/// </summary>
public sealed class MemorySandboxProvider : ISandboxProvider
{
    #region Private Fields

    private readonly ConcurrentDictionary<string, Machine> machines = new(StringComparer.Ordinal);

    #endregion

    #region Properties

    public string Kind => "memory";

    public ConcurrentQueue<CommandRequest> Commands { get; } = new();

    public IReadOnlyCollection<string> Ids => [.. machines.Keys];

    public IReadOnlyCollection<string> Tokens => [.. machines.Values.Select(machine => machine.Token)];

    public int Live => machines.Values.Count(machine => machine.State is not ProviderSandboxState.Gone);

    /// <summary>The first eight bytes of a PNG, then its header: a 1024 by 768 picture as far as its size goes.</summary>
    public static byte[] Picture { get; } = MakePicture();

    #endregion

    #region Public Methods

    public ProviderSandboxState StateOf(string id) => machines[id].State;

    #endregion

    #region Interface Implementations

    public Task<SandboxHandle> Create(SandboxSpec spec, CancellationToken cancellationToken)
    {
        var id = $"mem{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}";
        var machine = machines.GetOrAdd(id, _ => new Machine(spec.SpaceId, spec.SharingGroupId, $"tok_{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}"));
        return Task.FromResult(new SandboxHandle(id, machine.SpaceId, machine.GroupId, machine.Token));
    }

    public Task<SandboxInfo> Describe(SandboxHandle sandbox, CancellationToken cancellationToken) =>
        Task.FromResult(new SandboxInfo(
            machines.TryGetValue(sandbox.ProviderSandboxId, out var found) ? found.State : ProviderSandboxState.Gone,
            new Dictionary<string, string>(),
            null));

    public Task KeepAlive(SandboxHandle sandbox, TimeSpan idle, CancellationToken cancellationToken)
    {
        Find(sandbox);
        return Task.CompletedTask;
    }

    public Task Pause(SandboxHandle sandbox, CancellationToken cancellationToken)
    {
        Find(sandbox).State = ProviderSandboxState.Paused;
        return Task.CompletedTask;
    }

    public Task<SandboxHandle> Resume(SandboxHandle sandbox, TimeSpan idle, CancellationToken cancellationToken)
    {
        Find(sandbox).State = ProviderSandboxState.Running;
        return Task.FromResult(sandbox);
    }

    public Task Destroy(SandboxHandle sandbox, CancellationToken cancellationToken)
    {
        if (machines.TryGetValue(sandbox.ProviderSandboxId, out var found))
        {
            found.State = ProviderSandboxState.Gone;
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<CommandOutput> Run(SandboxHandle sandbox, CommandRequest command, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var machine = Enter(sandbox);
        Commands.Enqueue(command);
        await Task.Yield();
        yield return new CommandStarted(1);

        switch (command.Program)
        {
            case "scrot":
                machine.Files[command.Arguments[^1]] = Picture;
                break;
            case "rm":
                machine.Files.TryRemove(command.Arguments[^1], out _);
                break;
            case "/bin/bash" when command.Arguments[0] == "-l":
                yield return new CommandStdout(Encoding.UTF8.GetBytes($"{command.Arguments[^1]}\n"));
                break;
            default:
                break;
        }

        yield return new CommandExited(0, null);
    }

    public Task<Stream> ReadFile(SandboxHandle sandbox, string path, CancellationToken cancellationToken) =>
        Enter(sandbox).Files.TryGetValue(path, out var content)
            ? Task.FromResult<Stream>(new MemoryStream(content, writable: false))
            : throw new SandboxProviderException(SandboxFailure.NotFound, "Not found.");

    public async Task WriteFile(SandboxHandle sandbox, string path, Stream content, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        Enter(sandbox).Files[path] = copy.ToArray();
    }

    public Task<IReadOnlyList<SandboxEntry>> ListFiles(SandboxHandle sandbox, string path, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SandboxEntry>>([]);

    public Task SetNetworkPolicy(SandboxHandle sandbox, NetworkPolicy policy, CancellationToken cancellationToken)
    {
        Find(sandbox);
        return Task.CompletedTask;
    }

    #endregion

    #region Private Methods

    private Machine Find(SandboxHandle sandbox) =>
        machines.TryGetValue(sandbox.ProviderSandboxId, out var found) && found.State is not ProviderSandboxState.Gone
            && found.SpaceId == sandbox.SpaceId && found.GroupId == sandbox.SharingGroupId
            ? found
            : throw new SandboxProviderException(SandboxFailure.NotFound, "The sandbox does not exist.");

    private Machine Enter(SandboxHandle sandbox)
    {
        var found = Find(sandbox);

        return found.State is ProviderSandboxState.Running && found.Token == sandbox.AccessToken
            ? found
            : throw new SandboxProviderException(SandboxFailure.Rejected, "The sandbox is paused or refused the token.");
    }

    private static byte[] MakePicture()
    {
        var png = new byte[33];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(png, 0);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(8), 13);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 1024);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 768);
        return png;
    }

    #endregion

    #region Nested Types

    private sealed class Machine(Guid spaceId, Guid groupId, string token)
    {
        #region Properties

        public Guid SpaceId => spaceId;

        public Guid GroupId => groupId;

        public string Token => token;

        public ProviderSandboxState State { get; set; } = ProviderSandboxState.Running;

        public ConcurrentDictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

        #endregion
    }

    #endregion
}
