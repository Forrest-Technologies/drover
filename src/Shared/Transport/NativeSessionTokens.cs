namespace Drover.Transport;

internal sealed record NativeSessionTokens(string AccessToken, string RefreshToken, long ExpiresIn)
{
    #region Public Methods

    public override string ToString() => nameof(NativeSessionTokens);

    public static HttpContent RefreshContent(string refreshToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("refreshToken", refreshToken);
            writer.WriteEndObject();
        }

        return new ByteArrayContent(buffer.WrittenSpan.ToArray())
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
        };
    }

    public static async Task<NativeSessionTokens?> Read(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("accessToken", out var access) && access.ValueKind == JsonValueKind.String
            && root.TryGetProperty("refreshToken", out var refresh) && refresh.ValueKind == JsonValueKind.String
            && root.TryGetProperty("expiresIn", out var expires) && expires.ValueKind == JsonValueKind.Number
            && expires.TryGetInt64(out var seconds)
            ? new NativeSessionTokens(access.GetString()!, refresh.GetString()!, seconds)
            : null;
    }

    #endregion
}
