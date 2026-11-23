namespace Drover.Engine.Tests.Runs;

/// <summary>
/// A host that is stopped must finish stopping whatever the dispatcher was doing: waiting to claim again after a
/// failure, or in the middle of a claim that then fails. None of these tests measures time. A stop that does not
/// finish fails because the host's own 20 s wait for the dispatcher gives up, with the dispatcher's log.
/// </summary>
public sealed class EngineHostDisposeTests
{
    #region Private Fields

    // No database listens on this port, so every claim the dispatcher makes fails.
    private const string NoDatabase = "Host=127.0.0.1;Port=1;Username=none;Password=unused;Database=none";

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_host_stops_while_a_failed_claim_waits_to_be_retried()
    {
        await using var keys = new KeyRing();

        // The wait before a retry is an hour, so only the stop can end it. Before it observed the stop, this host
        // waited out the whole hour and its disposal gave up after 20 s.
        var host = Host(keys, options => options.ClaimRetryDelay = TimeSpan.FromHours(1));

        host.Start();
        await host.Log.Logged("Claiming runs failed").WaitAsync(patience, TestContext.Current.CancellationToken);

        var stop = async () => await host.DisposeAsync();

        await stop.Should().NotThrowAsync();
        host.Log.Tail().Should().Contain("stopped after a failed claim");
    }

    [Fact]
    public async Task A_claim_that_fails_after_the_stop_was_asked_for_does_not_fail_the_stop()
    {
        await using var keys = new KeyRing();
        using var claim = new BlockedClaim();
        var host = Host(keys, services: services => services.AddSingleton(new RunScopes(claim)));

        host.Start();
        await claim.Entered.WaitAsync(patience, TestContext.Current.CancellationToken);

        // The claim is in flight. Stop the host, and only then let the claim fail with an error that is not a
        // cancellation, such as a connection that breaks under it as the server shuts down.
        var stopping = host.DisposeAsync().AsTask();
        host.Stopping.IsCancellationRequested.Should().BeTrue();
        claim.Fail();

        var stop = async () => await stopping;

        await stop.Should().NotThrowAsync();
        host.Log.Tail().Should().Contain("failed to claim runs while stopping");
    }

    #endregion

    #region Private Methods

    private static EngineHost Host(
        KeyRing keys, Action<RunOptions>? configure = null, Action<IServiceCollection>? services = null) =>
        new(
            NoDatabase,
            keys.Path,
            new ScriptedModelProvider(new ScriptedModel()),
            [],
            configure,
            replaceServices: replace =>
            {
                replace.AddSingleton<IFileStore>(new MemoryFileStore());
                replace.AddSingleton<IToolSource>(new FakeToolServer());
                services?.Invoke(replace);
            });

    #endregion

    #region Types

    /// <summary>A scope factory whose first scope is not made until the test lets it fail.</summary>
    private sealed class BlockedClaim : IServiceScopeFactory, IDisposable
    {
        #region Private Fields

        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly ManualResetEventSlim failing = new();

        #endregion

        #region Properties

        /// <summary>Completes when the dispatcher is inside a claim.</summary>
        public Task Entered => entered.Task;

        #endregion

        #region Public Methods

        public void Fail() => failing.Set();

        #endregion

        #region Interface Implementations

        public IServiceScope CreateScope()
        {
            entered.TrySetResult();

            // The factory is a synchronous interface, so the claim waits here until the test lets it fail.
            failing.Wait(patience);
            throw new InvalidOperationException("The database went away under the claim.");
        }

        public void Dispose() => failing.Dispose();

        #endregion
    }

    private sealed class KeyRing : IAsyncDisposable
    {
        #region Properties

        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"drover-stop-{Guid.NewGuid():N}");

        #endregion

        #region Interface Implementations

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        #endregion
    }

    #endregion
}
