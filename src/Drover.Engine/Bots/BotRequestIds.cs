namespace Drover.Engine.Bots;

/// <summary>
/// The id of the bot that a create request with a retry identity makes. A client that sends a create again after the
/// answer was lost sends the same request id, and the server works out the same bot id from it, so the retry finds
/// the bot it made. The bot id is never the request id: it is worked out from the space, the maker and the request id
/// together, so two spaces, or two makers, that send the same request id get different bots and an id that is
/// held in another space never meets the global bot id key.
/// </summary>
public static class BotRequestIds
{
    #region Public Methods

    public static Guid For(Guid spaceId, AuditActor maker, Guid requestId)
    {
        Span<byte> input = stackalloc byte[16 * 4];
        spaceId.TryWriteBytes(input[..16]);
        maker.UserId.TryWriteBytes(input[16..32]);
        (maker.BotId ?? Guid.Empty).TryWriteBytes(input[32..48]);
        requestId.TryWriteBytes(input[48..]);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, hash);

        // Version 8 (custom) and the standard variant, so it reads as an ordinary Guid.
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16]);
    }

    #endregion
}
