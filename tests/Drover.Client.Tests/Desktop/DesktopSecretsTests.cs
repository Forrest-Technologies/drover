namespace Drover.Client.Tests.Desktop;

public sealed class DesktopSecretsTests
{
    #region Private Fields

    private const string Key = "drover.native.refresh:https://drover.example.test/";

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_sign_in_kept_in_the_system_store_is_there_after_a_restart()
    {
        var store = new SystemStore();
        var secret = NewSecret();

        await Open(store).SetAsync(Key, secret);

        store.Kept.Should().ContainKey(Key).WhoseValue.Should().Be(secret);
        (await Open(store).GetAsync(Key)).Should().Be(secret);
    }

    [Fact]
    public async Task Signing_out_removes_the_sign_in_from_the_system_store()
    {
        var store = new SystemStore();
        var secrets = Open(store);
        await secrets.SetAsync(Key, NewSecret());

        secrets.Remove(Key).Should().BeTrue();

        store.Kept.Should().BeEmpty();
        (await Open(store).GetAsync(Key)).Should().BeNull();
    }

    [Fact]
    public async Task The_system_store_is_opened_once_and_only_when_first_needed()
    {
        var store = new SystemStore();
        var opened = 0;
        var secrets = new DesktopSecrets(() =>
        {
            opened++;
            return store;
        }, NullLogger<DesktopSecrets>.Instance);

        opened.Should().Be(0);
        await secrets.SetAsync(Key, NewSecret());
        await secrets.GetAsync(Key);
        secrets.Remove(Key);

        opened.Should().Be(1);
    }

    [Fact]
    public async Task Without_a_system_store_the_sign_in_lasts_until_the_app_closes_and_the_app_says_so_once()
    {
        var log = new RecordedLog();
        var secrets = new DesktopSecrets(NoStore, log);
        var secret = NewSecret();

        await secrets.SetAsync(Key, secret);
        (await secrets.GetAsync(Key)).Should().Be(secret);
        secrets.Remove(Key).Should().BeTrue();
        (await secrets.GetAsync(Key)).Should().BeNull();

        log.Warnings.Should().ContainSingle()
            .Which.Should().Be("Drover cannot use a credential store on this computer, so the sign-in lasts until the app closes");
        (await new DesktopSecrets(NoStore, new RecordedLog()).GetAsync(Key)).Should().BeNull();
    }

    [Fact]
    public async Task A_store_that_fails_later_keeps_this_sign_in_until_the_app_closes_and_says_so_once()
    {
        var store = new SystemStore();
        var log = new RecordedLog();
        var secrets = new DesktopSecrets(() => store, log);
        await secrets.SetAsync(Key, NewSecret());
        store.Broken = true;
        var renewed = NewSecret();

        await secrets.SetAsync(Key, renewed);
        (await secrets.GetAsync(Key)).Should().Be(renewed);
        secrets.Remove(Key).Should().BeTrue();

        log.Warnings.Should().ContainSingle();
    }

    [Fact]
    public async Task No_secret_reaches_the_log()
    {
        var secret = NewSecret();
        var kept = new RecordedLog();
        var store = new SystemStore();
        var keeping = new DesktopSecrets(() => store, kept);
        await keeping.SetAsync(Key, secret);
        await keeping.GetAsync(Key);
        store.Broken = true;
        await keeping.SetAsync(Key, secret);
        var forgotten = new RecordedLog();
        await new DesktopSecrets(NoStore, forgotten).SetAsync(Key, secret);

        kept.Lines.Concat(forgotten.Lines).Should().NotBeEmpty()
            .And.AllSatisfy(line => line.Should().NotContain(secret));
    }

    #endregion

    #region Private Methods

    private static DesktopSecrets Open(SystemStore store) => new(() => store, NullLogger<DesktopSecrets>.Instance);

    private static ISecureStorage NoStore() =>
        throw new InvalidOperationException("No Secret Service is running on this test computer.");

    private static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    #endregion

    #region Test Doubles

    /// <summary>Stands in for the operating system's store, which outlives any one instance of the app.</summary>
    private sealed class SystemStore : ISecureStorage
    {
        #region Properties

        public Dictionary<string, string> Kept { get; } = [];

        public bool Broken { get; set; }

        #endregion

        #region Interface Implementations

#pragma warning disable DRV0001 // Matches the storage API used by the linked source.
        public Task<string?> GetAsync(string key) => Task.FromResult(Working().Kept.GetValueOrDefault(key));

        public Task SetAsync(string key, string value)
        {
            Working().Kept[key] = value;
            return Task.CompletedTask;
        }
#pragma warning restore DRV0001

        public bool Remove(string key) => Working().Kept.Remove(key);

        #endregion

        #region Private Methods

        private SystemStore Working() => Broken ? throw new IOException("The keyring was locked.") : this;

        #endregion
    }

    private sealed class RecordedLog : ILogger<DesktopSecrets>
    {
        #region Properties

        public List<string> Lines { get; } = [];

        public List<string> Warnings { get; } = [];

        #endregion

        #region Interface Implementations

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var line = $"{formatter(state, exception)} {exception}";
            Lines.Add(line);
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }

        #endregion
    }

    #endregion
}
