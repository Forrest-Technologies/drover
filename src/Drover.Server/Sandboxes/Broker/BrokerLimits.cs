namespace Drover.Server.Sandboxes.Broker;

/// <summary>
/// What one sandbox may take from the broker: connections open at once and bytes an hour, both from the deployment
/// policy; and how many wrong tokens one address may send a minute. In this process's memory.
/// </summary>
public sealed class BrokerLimits(IOptions<DeploymentPolicyOptions> deployment, TimeProvider time)
{
    #region Private Fields

    private const int FailuresPerMinute = 30;

    private const int UnprovenPerAddress = 32;

    private const int MaxTracked = 50_000;

    private static readonly TimeSpan validFor = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, int> unproven = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, DateTimeOffset> valid = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<Guid, int> open = new();

    private readonly ConcurrentDictionary<Guid, (DateTimeOffset Hour, long Bytes)> spent = new();

    private readonly ConcurrentDictionary<string, (DateTimeOffset Minute, int Count)> failures = new(StringComparer.Ordinal);

    #endregion

    #region Public Methods

    /// <summary>A place among the sandbox's open connections, released on dispose, or null when it has as many as it may.</summary>
    public IDisposable? Enter(Guid sandboxId)
    {
        var most = deployment.Value.Rules.MaxBrokerConnectionsPerSandbox;

        if (open.AddOrUpdate(sandboxId, 1, (_, count) => count + 1) > most)
        {
            Leave(sandboxId);
            return null;
        }

        return new Place(this, sandboxId);
    }

    /// <summary>Counts bytes against the sandbox's hour. False once the hour's allowance is used up.</summary>
    public bool Spend(Guid sandboxId, int bytes)
    {
        var hour = Hour(time.GetUtcNow());
        var allowed = (long)deployment.Value.Rules.MaxBrokerMegabytesPerSandboxPerHour * 1024 * 1024;
        var now = spent.AddOrUpdate(sandboxId, (hour, bytes), (_, current) => current.Hour == hour ? (hour, current.Bytes + bytes) : (hour, bytes));
        return now.Bytes <= allowed;
    }

    /// <summary>
    /// Whether a token may be checked against the database now. A token proven in the last minutes always may, so
    /// strangers who share a sandbox's address cannot lock it out; another may unless its address has sent too many
    /// wrong ones this minute. An IPv6 address counts by its /64, which one machine usually holds whole.
    /// </summary>
    public bool MayTry(string remote, string tokenKey) =>
        valid.TryGetValue(tokenKey, out var until) && until > time.GetUtcNow()
        || !failures.TryGetValue(Key(remote), out var current) || current.Minute != Minute(time.GetUtcNow()) || current.Count < FailuresPerMinute;

    public void Failed(string remote)
    {
        var minute = Minute(time.GetUtcNow());
        Evict();
        failures.AddOrUpdate(Key(remote), (minute, 1), (_, current) => current.Minute == minute ? (minute, current.Count + 1) : (minute, 1));
    }

    public void Proven(string tokenKey)
    {
        Evict();
        valid[tokenKey] = time.GetUtcNow() + validFor;
    }

    /// <summary>A place among an address's connections that have not yet shown a token, or null when it has as many as it may.</summary>
    public IDisposable? EnterUnproven(string remote)
    {
        var key = Key(remote);

        if (unproven.AddOrUpdate(key, 1, (_, count) => count + 1) > UnprovenPerAddress)
        {
            unproven.AddOrUpdate(key, 0, (_, count) => Math.Max(count - 1, 0));
            return null;
        }

        return new Unproven(this, key);
    }

    #endregion

    #region Private Methods

    private static DateTimeOffset Hour(DateTimeOffset at) => new(at.Year, at.Month, at.Day, at.Hour, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Minute(DateTimeOffset at) => new(at.Year, at.Month, at.Day, at.Hour, at.Minute, 0, TimeSpan.Zero);

    private void Leave(Guid sandboxId) => open.AddOrUpdate(sandboxId, 0, (_, count) => Math.Max(count - 1, 0));

    private static string Key(string remote) =>
        IPAddress.TryParse(remote, out var address) && address.AddressFamily is AddressFamily.InterNetworkV6 && !address.IsIPv4MappedToIPv6
            ? $"{Convert.ToHexString(address.GetAddressBytes(), 0, 8)}/64"
            : remote;

    /// <summary>Forgets counts that no longer matter, once the tables grow large, so they stay bounded.</summary>
    private void Evict()
    {
        var now = time.GetUtcNow();

        if (failures.Count > MaxTracked)
        {
            foreach (var (key, entry) in failures.Where(entry => entry.Value.Minute != Minute(now)))
            {
                failures.TryRemove(key, out _);
            }
        }

        if (valid.Count > MaxTracked)
        {
            foreach (var (key, _) in valid.Where(entry => entry.Value <= now))
            {
                valid.TryRemove(key, out _);
            }
        }

        if (spent.Count > MaxTracked)
        {
            foreach (var (key, _) in spent.Where(entry => entry.Value.Hour != Hour(now)))
            {
                spent.TryRemove(key, out _);
            }
        }
    }

    #endregion

    #region Nested Types

    private sealed class Unproven(BrokerLimits limits, string key) : IDisposable
    {
        #region Private Fields

        private int left;

        #endregion

        #region Interface Implementations

        public void Dispose()
        {
            if (Interlocked.Exchange(ref left, 1) == 0)
            {
                limits.unproven.AddOrUpdate(key, 0, (_, count) => Math.Max(count - 1, 0));
            }
        }

        #endregion
    }

    private sealed class Place(BrokerLimits limits, Guid sandboxId) : IDisposable
    {
        #region Private Fields

        private int left;

        #endregion

        #region Interface Implementations

        public void Dispose()
        {
            if (Interlocked.Exchange(ref left, 1) == 0)
            {
                limits.Leave(sandboxId);
            }
        }

        #endregion
    }

    #endregion
}
