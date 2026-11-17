namespace Drover.Adapters.E2B;

/// <summary>
/// Connect protocol framing for server-streaming calls: one flag byte, a four-byte big-endian length, then the JSON
/// message. The last frame has the end-of-stream flag and carries <c>{}</c> or <c>{"error":{...}}</c>. The layout comes
/// from the Connect protocol specification and is pinned by a stream captured from E2B.
/// </summary>
internal static class ConnectEnvelope
{
    #region Private Fields

    private const byte CompressedFlag = 0x01;

    private const byte EndStreamFlag = 0x02;

    private const int HeaderBytes = 5;

    /// <summary>A command's output arrives in small frames. Anything this large is not a frame Drover sent for.</summary>
    private const int MaxMessageBytes = 16 * 1024 * 1024;

    #endregion

    #region Public Methods

    public static byte[] Frame(ReadOnlySpan<byte> message)
    {
        var frame = new byte[HeaderBytes + message.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1), message.Length);
        message.CopyTo(frame.AsSpan(HeaderBytes));

        return frame;
    }

    /// <summary>
    /// The messages of a stream, in order. An error at the end of the stream is thrown as the mapped failure; a stream
    /// that stops without its end frame is a failure too, because the answer is incomplete.
    /// </summary>
    public static async IAsyncEnumerable<JsonElement> Read(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var header = new byte[HeaderBytes];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Fill(stream, header, cancellationToken);

            var flags = header[0];
            var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1));

            if ((flags & CompressedFlag) != 0 || length > MaxMessageBytes)
            {
                throw FailureMapping.Malformed();
            }

            var message = new byte[length];
            await Fill(stream, message, cancellationToken);
            var root = Parse(message);

            if ((flags & EndStreamFlag) != 0)
            {
                ThrowIfError(root);
                yield break;
            }

            yield return root;
        }
    }

    #endregion

    #region Private Methods

    private static async Task Fill(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);

        if (read < buffer.Length)
        {
            throw FailureMapping.EndedEarly();
        }
    }

    private static JsonElement Parse(byte[] message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw FailureMapping.Malformed();
        }
    }

    private static void ThrowIfError(JsonElement end)
    {
        if (end.ValueKind == JsonValueKind.Object && end.TryGetProperty("error", out var error))
        {
            throw FailureMapping.FromConnectCode(
                error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var code)
                    && code.ValueKind == JsonValueKind.String
                    ? code.GetString()
                    : null);
        }
    }

    #endregion
}
