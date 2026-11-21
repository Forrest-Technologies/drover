namespace Drover.Engine.Tests.Runs;

/// <summary>A listener that is stopped must release its connection and end, whatever the notification stream does.</summary>
public sealed class RunWakeListenerTests(PostgresServer postgres) : IAsyncLifetime
{
    #region Private Fields

    private TestDatabase database = null!;

    #endregion

    #region Public Methods

    public async ValueTask InitializeAsync() => database = await postgres.CreateDatabase();

    public async ValueTask DisposeAsync() => await database.DisposeAsync();

    [Fact]
    public async Task Stopping_the_listener_ends_the_wait()
    {
        var listener = new Drover.Data.Runs.RunWakeListener(database.AppConnectionString, NullLogger<Drover.Data.Runs.RunWakeListener>.Instance);
        using var stopping = new CancellationTokenSource();

        var listening = listener.Listen(() => { }, stopping.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        await stopping.CancelAsync();

        await listening.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }

    #endregion
}
