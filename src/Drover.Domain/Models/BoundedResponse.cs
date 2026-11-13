namespace Drover.Domain.Models;

/// <summary>Reads a provider's JSON answer without trusting its size.</summary>
public static class BoundedResponse
{
    #region Public Methods

    /// <summary>
    /// Parses the body, refusing it once it passes <paramref name="maxBytes"/>. The request must have been sent
    /// with <see cref="HttpCompletionOption.ResponseHeadersRead"/>, or the whole body is already in memory.
    /// </summary>
    public static async Task<JsonDocument> ReadJson(HttpContent content, int maxBytes, string what, CancellationToken cancellation)
    {
        if (content.Headers.ContentLength > maxBytes)
        {
            throw ModelProviderException.FromLimit(what);
        }

        await using var body = await content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;

        while ((read = await body.ReadAsync(chunk, cancellation)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw ModelProviderException.FromLimit(what);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellation);
        }

        buffer.Position = 0;

        return await JsonDocument.ParseAsync(buffer, cancellationToken: cancellation);
    }

    #endregion
}
