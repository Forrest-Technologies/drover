namespace Drover.Domain.Files;

/// <summary>
/// The one shape a storage key has: the space's id and the file's id, each as 32 lowercase hexadecimal digits,
/// joined by a slash. Nothing a person typed is ever part of a key, so a key holds no dots, no separators of its
/// own, and no name, and a key of one space can never be read as another's.
/// </summary>
public static class FileStorageKeys
{
    #region Private Fields

    private const int IdLength = 32;

    #endregion

    #region Public Methods

    public static string For(Guid spaceId, Guid fileId) => $"{spaceId:N}/{fileId:N}";

    /// <summary>True when the key has the one shape a key has and names this space.</summary>
    public static bool BelongsTo(Guid spaceId, string? storageKey) =>
        storageKey is { Length: IdLength * 2 + 1 }
        && storageKey[IdLength] == '/'
        && IsId(storageKey.AsSpan(0, IdLength))
        && IsId(storageKey.AsSpan(IdLength + 1))
        && storageKey.AsSpan(0, IdLength).SequenceEqual(spaceId.ToString("N"));

    /// <summary>The file's id in a key of this space. Throws when the key is not one of this space's.</summary>
    public static Guid Require(Guid spaceId, string? storageKey) =>
        BelongsTo(spaceId, storageKey)
            ? Guid.ParseExact(storageKey.AsSpan(IdLength + 1), "N")
            : throw new ArgumentException("The storage key is not a key of this space.", nameof(storageKey));

    #endregion

    #region Private Methods

    private static bool IsId(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    #endregion
}
