namespace Drover.Engine.Web;

/// <summary>One page as a bot is given it: the address it ended at, its title, its readable text, and whether it was cut.</summary>
public sealed record WebPage(Uri Url, string? Title, string Text, bool IsCut);


/// <summary>
/// Reads one web page for a bot. Its client must come from <see cref="OutboundHttp.CreateHandlerWithoutRedirects"/>
/// and never state a space's rule, so every connection it opens is checked and refused when the host resolves to a
/// private, loopback, link-local, or metadata address, whatever the deployment allows for model and tool servers:
/// an address a model chose is never trusted to reach the server's own network. Redirects are followed here, at most
/// <see cref="MaxRedirects"/>, and each new address is checked as the first was. The whole read, redirects included,
/// stops at <see cref="DeploymentPolicy.MaxWebRequestSeconds"/>, and no more than
/// <see cref="DeploymentPolicy.MaxWebPageBytes"/> of a page is read.
/// </summary>
public sealed class WebPageReader(HttpClient client, IOptions<DeploymentPolicyOptions> deployment, ILogger<WebPageReader> logger)
{
    #region Private Fields

    private static readonly FrozenSet<string> htmlTypes = new[] { "text/html", "application/xhtml+xml" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> textTypes = new[]
    {
        "application/json", "application/xml", "application/rss+xml", "application/atom+xml", "application/javascript",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Public Methods

    /// <summary>The page at the address, or a refusal in words the model can act on.</summary>
    public async Task<WebPage> Read(Uri address, CancellationToken cancellationToken)
    {
        var rules = deployment.Value.Rules;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(rules.MaxWebRequestSeconds));
        var current = address;

        try
        {
            for (var redirects = 0; ; redirects++)
            {
                EnsureReadable(current);
                using var response = await Get(current, timeout.Token);

                if (RedirectTarget(current, response) is not { } next)
                {
                    return await ReadPage(current, response, rules.MaxWebPageBytes, timeout.Token);
                }

                if (redirects == MaxRedirects)
                {
                    throw new BuiltinToolException($"The page redirected more than {MaxRedirects} times. It was not read.");
                }

                current = next;
            }
        }
        catch (OutboundBlockedException)
        {
            throw new BuiltinToolException(
                $"{current.Host} is a private, loopback, or link-local address. Web pages are read only from public addresses.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new BuiltinToolException($"The page took longer than {rules.MaxWebRequestSeconds} seconds to read. It was not read.");
        }
        catch (HttpRequestException failure)
        {
            logger.LogInformation("A web page on {Host} could not be reached: {Error}", current.Host, failure.HttpRequestError);
            throw new BuiltinToolException($"The page at {current.Host} could not be reached.");
        }
    }

    /// <summary>
    /// Refuses an address that is not a plain public web address before any request is made: only http and https, and
    /// no name and password in it. Whether the host is private is decided when it is connected to, after it resolves.
    /// </summary>
    public static void EnsureReadable(Uri address)
    {
        if (!address.IsAbsoluteUri || address.Scheme is not ("http" or "https"))
        {
            throw new BuiltinToolException("Only http and https addresses can be read.");
        }

        if (address.UserInfo.Length > 0)
        {
            throw new BuiltinToolException("An address with a name or password in it is not read.");
        }
    }

    #endregion

    #region Private Methods

    private async Task<HttpResponseMessage> Get(Uri address, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Accept.ParseAdd("text/html, application/xhtml+xml, text/plain;q=0.9, */*;q=0.5");
        request.Headers.UserAgent.ParseAdd("Drover/1.0 (web.fetch)");
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static Uri? RedirectTarget(Uri current, HttpResponseMessage response)
    {
        if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))
        {
            return null;
        }

        return response.Headers.Location is { } location
            ? new Uri(current, location)
            : throw new BuiltinToolException("The page redirected without saying where to.");
    }

    private static async Task<WebPage> ReadPage(Uri address, HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new BuiltinToolException($"The page answered with status {(int)response.StatusCode}.");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "text/html";
        var isHtml = htmlTypes.Contains(mediaType);

        if (!isHtml && !IsText(mediaType))
        {
            throw new BuiltinToolException($"The page is {mediaType}, not text. Only web pages and plain text are read.");
        }

        var (bytes, bytesCut) = await ReadAtMost(response.Content, maxBytes, cancellationToken);
        var decoded = EncodingOf(response.Content.Headers.ContentType?.CharSet).GetString(bytes);
        var (title, text) = isHtml ? ReadableText.FromHtml(decoded) : (null, ReadableText.FromPlainText(decoded));
        var textCut = text.Length > MaxTextCharacters;
        return new WebPage(address, title, textCut ? text[..MaxTextCharacters] : text, bytesCut || textCut);
    }

    private static bool IsText(string mediaType) =>
        mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || textTypes.Contains(mediaType)
        || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>The first <paramref name="maxBytes"/> of the body, and whether there was more.</summary>
    private static async Task<(byte[] Bytes, bool IsCut)> ReadAtMost(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        await using var body = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maxBytes];
        var total = 0;
        int read;

        while (total < maxBytes && (read = await body.ReadAsync(buffer.AsMemory(total, maxBytes - total), cancellationToken)) > 0)
        {
            total += read;
        }

        var isCut = total == maxBytes && await body.ReadAsync(new byte[1], cancellationToken) > 0;
        return (buffer[..total], isCut);
    }

    /// <summary>The encoding the answer names, or UTF-8 when it names none this runtime knows.</summary>
    private static Encoding EncodingOf(string? charset) =>
        Encoding.GetEncodings()
            .FirstOrDefault(info => string.Equals(info.Name, charset?.Trim('"', ' '), StringComparison.OrdinalIgnoreCase))
            ?.GetEncoding()
        ?? Encoding.UTF8;

    #endregion

    #region Constants

    public const string HttpClientName = "Drover.WebPages";

    public const int MaxRedirects = 5;

    /// <summary>The most characters of a page's text a bot is given, so one page cannot fill a model's context.</summary>
    public const int MaxTextCharacters = 20_000;

    #endregion
}
