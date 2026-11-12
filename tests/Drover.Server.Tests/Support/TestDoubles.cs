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

    private int checks;

    #endregion

    #region Properties

    public int Checks => Volatile.Read(ref checks);

    #endregion

    #region Public Methods

    public void Add() => Interlocked.Increment(ref checks);

    #endregion
}

/// <summary>Identity's default hasher, counting each check.</summary>
public sealed class CountingPasswordHasher(HashCounter counter) : IPasswordHasher<User>
{
    #region Private Fields

    private readonly PasswordHasher<User> inner = new();

    #endregion

    #region Interface Implementations

    public string HashPassword(User user, string password) => inner.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
    {
        counter.Add();
        return inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }

    #endregion
}
