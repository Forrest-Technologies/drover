namespace Drover.Engine.Tests.Runs;

/// <summary>A host whose database claims keep failing must still stop immediately when it is disposed.</summary>
public sealed class EngineHostDisposeTests
{
    #region Public Methods

    [Fact]
    public async Task A_host_stops_when_a_failed_claim_is_waiting_to_be_retried()
    {
        var keyRing = Path.Combine(Path.GetTempPath(), $"drover-stop-{Guid.NewGuid():N}");

        // No server on this port: every claim fails, so the dispatcher is in its retry pause when we stop it.
        var host = new EngineHost(
            "Host=127.0.0.1;Port=1;Username=none;Password=unused;Database=none",
            keyRing,
            new ScriptedModelProvider(new ScriptedModel()),
            [],
            replaceServices: services =>
            {
                services.AddSingleton<IFileStore>(new MemoryFileStore());
                services.AddSingleton<IToolSource>(new FakeToolServer());
            });

        try
        {
            host.Start();
            await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
            var stopping = Stopwatch.StartNew();

            await host.DisposeAsync();

            stopping.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(800));
        }
        finally
        {
            if (Directory.Exists(keyRing))
            {
                Directory.Delete(keyRing, recursive: true);
            }
        }
    }

    #endregion
}
