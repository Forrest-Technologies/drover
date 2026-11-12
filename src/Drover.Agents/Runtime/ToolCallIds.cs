namespace Drover.Agents.Runtime;

/// <summary>
/// The <c>tool_calls</c> row id for a model's call id within a run. Model call ids are strings and differ by
/// provider; deriving the row id from them lets every part of a run find the same row without a lookup column.
/// </summary>
public static class ToolCallIds
{
    #region Public Methods

    public static Guid For(Guid runId, string callId)
    {
        var hash = SHA256.HashData([.. runId.ToByteArray(), .. Encoding.UTF8.GetBytes(callId)]);
        var bytes = hash[..16];

        // RFC 9562 version 8 (custom) and the standard variant, so the id is a well-formed UUID.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    #endregion
}
