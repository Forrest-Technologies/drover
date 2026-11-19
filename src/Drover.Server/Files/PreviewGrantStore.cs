namespace Drover.Server.Files;

/// <summary>Short-lived file grants. Only token hashes are retained, and taking one removes it atomically.</summary>
public sealed class PreviewGrantStore(TimeProvider time)
{
    #region Private Fields

    private const int MaxOutstanding = 10_000;

    private static readonly TimeSpan lifetime = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, PreviewGrant> grants = [];

    private readonly object sync = new();

    private DateTimeOffset nextSweep;

    #endregion

    #region Public Methods

    public string? Add(Guid spaceId, Guid userId, Guid fileId, string sha256)
    {
        lock (sync)
        {
            var now = time.GetUtcNow();
            if (now >= nextSweep)
            {
                RemoveExpired(now);
                nextSweep = now + TimeSpan.FromSeconds(1);
            }

            if (grants.Count >= MaxOutstanding)
            {
                return null;
            }

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            grants.Add(Hash(token), new(spaceId, userId, fileId, sha256, now + lifetime));
            return token;
        }
    }

    public PreviewGrant? Take(string token)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit))
        {
            return null;
        }

        lock (sync)
        {
            if (!grants.Remove(Hash(token), out var grant) || grant.ExpiresAt <= time.GetUtcNow())
            {
                return null;
            }

            return grant;
        }
    }

    public void Revoke(string? token, Guid spaceId, Guid userId, Guid fileId)
    {
        if (token is not { Length: 64 } || !token.All(Uri.IsHexDigit))
        {
            return;
        }

        var hash = Hash(token);
        lock (sync)
        {
            if (grants.TryGetValue(hash, out var grant)
                && (grant.SpaceId, grant.UserId, grant.FileId) == (spaceId, userId, fileId))
            {
                grants.Remove(hash);
            }
        }
    }

    #endregion

    #region Private Methods

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var hash in grants.Where(item => item.Value.ExpiresAt <= now).Select(item => item.Key).ToArray())
        {
            grants.Remove(hash);
        }
    }

    #endregion
}


public sealed record PreviewGrant(Guid SpaceId, Guid UserId, Guid FileId, string Sha256, DateTimeOffset ExpiresAt);
