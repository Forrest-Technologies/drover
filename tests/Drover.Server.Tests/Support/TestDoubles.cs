namespace Drover.Server.Tests.Support;

/// <summary>A clock a test moves by hand, so a five-minute interval takes no time.</summary>
public sealed class AdjustableTimeProvider : TimeProvider
{
    #region Private Fields

    private DateTimeOffset now = DateTimeOffset.UtcNow;

    #endregion

    #region Public Methods

    public override DateTimeOffset GetUtcNow() => now;

    #endregion

    #region Public Methods

    public void Advance(TimeSpan by) => now += by;

    #endregion
}

/// <summary>How many times a password was checked against a hash.</summary>
public sealed class HashCounter
{
    #region Private Fields

    private readonly ConcurrentDictionary<string, int> byHash = new(StringComparer.Ordinal);

    private int checks;

    private HashHold? next;

    #endregion

    #region Properties

    public int Checks => Volatile.Read(ref checks);

    #endregion

    #region Public Methods

    public void Add(string hashedPassword)
    {
        Interlocked.Increment(ref checks);
        byHash.AddOrUpdate(hashedPassword, 1, (_, count) => count + 1);
    }

    /// <summary>How many guesses were checked against this one stored hash, as opposed to the decoy.</summary>
    public int ChecksAgainst(string hashedPassword) => byHash.GetValueOrDefault(hashedPassword);

    /// <summary>Makes the next check or hash wait in the hasher until released, so a test can look around.</summary>
    public HashHold HoldNextCheck()
    {
        var hold = new HashHold();
        Interlocked.Exchange(ref next, hold);
        return hold;
    }

    /// <summary>Called by the hasher: waits if a test asked to hold this check.</summary>
    public void WaitIfHeld() => Interlocked.Exchange(ref next, null)?.Wait();

    #endregion
}

/// <summary>Identity's default hasher, counting each check.</summary>
public sealed class CountingPasswordHasher(HashCounter counter) : IPasswordHasher<User>
{
    #region Private Fields

    private readonly PasswordHasher<User> inner = new();

    #endregion

    #region Interface Implementations

    public string HashPassword(User user, string password)
    {
        counter.WaitIfHeld();
        return inner.HashPassword(user, password);
    }

    public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
    {
        counter.WaitIfHeld();
        counter.Add(hashedPassword);
        return inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }

    #endregion
}


/// <summary>A password check held open by a test: <see cref="Entered"/> completes when the check is waiting.</summary>
public sealed class HashHold : IDisposable
{
    #region Private Fields

    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly ManualResetEventSlim released = new();

    #endregion

    #region Properties

    public Task Entered => entered.Task;

    #endregion

    #region Interface Implementations

    public void Dispose() => released.Set();

    #endregion

    #region Public Methods

    public void Wait()
    {
        entered.TrySetResult();
        released.Wait(TimeSpan.FromSeconds(30));
    }

    #endregion
}
