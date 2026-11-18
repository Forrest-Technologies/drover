namespace Drover.Server.Clients;

/// <summary>
/// Serves the copy of a client file that the build compressed ahead of time, when the browser accepts its encoding.
/// The native client's framework is tens of megabytes as files and a fraction of that compressed, and the server
/// does no compression of its own. The file is answered with the content type of the file asked for, so the
/// browser reads it as that file; nothing else about the answer changes.
/// </summary>
internal sealed class PrecompressedFiles(RequestDelegate next, StaticFileOptions files)
{
    #region Private Fields

    private const string Chosen = "drover.precompressed";

    // Brotli first: it is the smaller of the two the build writes.
    private static readonly (string Encoding, string Suffix)[] encodings = [("br", ".br"), ("gzip", ".gz")];

    #endregion

    #region Public Methods

    public Task Invoke(HttpContext context)
    {
        var request = context.Request;

        if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            && request.Path.StartsWithSegments(files.RequestPath, out var rest)
            && rest.Value is { Length: > 1 } path
            && files.ContentTypeProvider.TryGetContentType(path, out var contentType)
            && Compressed(request, path) is { } compressed)
        {
            request.Path = new PathString($"{request.Path.Value}{compressed.Suffix}");
            context.Items[Chosen] = new Choice(contentType, compressed.Encoding);
        }

        return next(context);
    }

    /// <summary>Gives a compressed answer the type of the file it stands for, and says how it is encoded.</summary>
    public static void Describe(StaticFileResponseContext file)
    {
        var response = file.Context.Response;

        // A cache in front of the server keeps the plain and the compressed answer apart.
        response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptEncoding);

        if (file.Context.Items.TryGetValue(Chosen, out var chosen) && chosen is Choice choice)
        {
            response.ContentType = choice.ContentType;
            response.Headers.ContentEncoding = choice.Encoding;
        }
    }

    #endregion

    #region Private Methods

    private (string Encoding, string Suffix)? Compressed(HttpRequest request, string path)
    {
        var accepted = request.GetTypedHeaders().AcceptEncoding;

        foreach (var (encoding, suffix) in encodings)
        {
            if (accepted.Any(value => value.Value.Equals(encoding, StringComparison.OrdinalIgnoreCase) && value.Quality is null or > 0)
                && files.FileProvider!.GetFileInfo($"{path}{suffix}").Exists)
            {
                return (encoding, suffix);
            }
        }

        return null;
    }

    #endregion

    #region Nested Types

    private sealed record Choice(string ContentType, string Encoding);

    #endregion
}
