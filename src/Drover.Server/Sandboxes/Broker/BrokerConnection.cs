namespace Drover.Server.Sandboxes.Broker;

/// <summary>
/// One connection from a sandbox to the credential broker, after TLS. The sandbox proves which sandbox it is with the
/// token in its environment. A tunnel to a host no secret is bound to is relayed blind. A host a secret is bound to is
/// answered with a certificate from the space's authority, and each request on it has its placeholders replaced with
/// real values, only where the secret allows, and real values replaced back in what returns. Plain HTTP is relayed
/// for hosts no secret is bound to and refused for the rest, so a value is never sent unencrypted. Nothing about a
/// request but its host and the outcome is logged.
/// </summary>
public sealed class BrokerConnection(
    CredentialBrokerService broker,
    BrokerUpstream upstream,
    BrokerLimits limits,
    IReadOnlyCollection<int> allowedPorts,
    ILogger logger)
{
    #region Private Fields

    private const int MaxBodyBytes = 4 * 1024 * 1024;

    private const int SecurePort = 443;

    private const int ScrubbedBodyBytes = 1024 * 1024;

    private static readonly TimeSpan idle = TimeSpan.FromMinutes(2);

    // Compared without case: header names are.
    private static readonly HashSet<string> hopByHop =
    [
        "connection", "keep-alive", "proxy-authorization", "proxy-authenticate", "proxy-connection", "te", "trailer",
        "transfer-encoding", "upgrade", "host", "content-length", "expect", "accept-encoding", "range", "if-range",
    ];

    #endregion

    #region Public Methods

    public async Task Serve(Stream client, string remote, CancellationToken cancellationToken)
    {
        var reader = new BrokerHttpReader(client);
        BrokerRequestHead? head;
        using var unproven = limits.EnterUnproven(remote);

        if (unproven is null)
        {
            await Answer(client, 429, "Too many connections from this address have not shown a token.", cancellationToken);
            return;
        }

        try
        {
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waiting.CancelAfter(TimeSpan.FromSeconds(10));
            head = await reader.ReadHead(waiting.Token);
        }
        catch (BrokerHttpException failure)
        {
            logger.LogDebug("Broker: {Failure}", failure.GetType().Name);
            await Answer(client, 400, failure.Message, cancellationToken);
            return;
        }

        if (head is null)
        {
            return;
        }

        if (await Authenticate(head, remote, cancellationToken) is not { } session)
        {
            await Answer(client, 407, "Give the sandbox's broker token.", cancellationToken, ("Proxy-Authenticate", "Basic realm=\"drover\""));
            return;
        }

        // Shown a token, the connection no longer counts against its address's strangers.
        unproven.Dispose();
        using var slot = limits.Enter(session.SandboxId);

        if (slot is null)
        {
            await Answer(client, 429, "This sandbox has as many connections open as it may.", cancellationToken);
            return;
        }

        if (head.Method is "CONNECT")
        {
            await Connect(client, head, session, cancellationToken);
        }
        else if (Uri.TryCreate(head.Target, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttp)
        {
            await RelayPlain(client, reader, head, address, session, cancellationToken);
        }
        else
        {
            await Answer(client, 405, "The broker takes CONNECT, or a plain HTTP request with a full address.", cancellationToken);
        }
    }

    #endregion

    #region Connecting

    private async Task<BrokerSession?> Authenticate(BrokerRequestHead head, string remote, CancellationToken cancellationToken)
    {
        if (head.Only("Proxy-Authorization") is not { } given || !given.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tokenKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(given)));

        if (!limits.MayTry(remote, tokenKey))
        {
            return null;
        }

        string pair;

        try
        {
            pair = Encoding.UTF8.GetString(Convert.FromBase64String(given[6..].Trim()));
        }
        catch (FormatException unreadable)
        {
            logger.LogDebug("Broker: {Failure}", unreadable.GetType().Name);
            limits.Failed(remote);
            return null;
        }

        var colon = pair.IndexOf(':', StringComparison.Ordinal);

        if (colon < 0 || pair[..colon] != BrokerTokens.ProxyUser || await broker.Authenticate(pair[(colon + 1)..], cancellationToken) is not { } session)
        {
            limits.Failed(remote);
            return null;
        }

        limits.Proven(tokenKey);
        return session;
    }

    private async Task Connect(Stream client, BrokerRequestHead head, BrokerSession session, CancellationToken cancellationToken)
    {
        if (!TryReadAuthority(head.Target, out var host, out var port))
        {
            await Answer(client, 400, "CONNECT needs a host and a port.", cancellationToken);
            return;
        }

        if (!allowedPorts.Contains(port))
        {
            await Answer(client, 403, $"The broker relays to ports {string.Join(", ", allowedPorts)} only.", cancellationToken);
            return;
        }

        if (!session.MayReach(host))
        {
            Log(session, host, "refused by the bot's network setting");
            await Answer(client, 403, $"This bot's network setting does not let its sandbox reach {host}.", cancellationToken);
            return;
        }

        if (session.IsBound(host))
        {
            if (port != SecurePort)
            {
                await Answer(client, 403, $"A secret is bound to {host}, so the broker reaches it on port {SecurePort} only.", cancellationToken);
                return;
            }

            await Intercept(client, host, port, session, cancellationToken);
            return;
        }

        Stream remote;

        try
        {
            remote = await upstream.Connect(host, port, cancellationToken);
        }
        catch (Exception failure) when (failure is OutboundBlockedException or HttpRequestException or SocketException)
        {
            logger.LogDebug("Broker: {Failure}", failure.GetType().Name);
            Log(session, host, "could not be reached");
            await Answer(client, 502, $"{host} could not be reached.", cancellationToken);
            return;
        }

        await using (remote)
        {
            await Write(client, "HTTP/1.1 200 Connection established\r\n\r\n", cancellationToken);
            var bytes = await Pump(client, remote, session.SandboxId, cancellationToken);
            Log(session, host, $"tunnel closed after {bytes} bytes");
        }
    }

    private async Task RelayPlain(Stream client, BrokerHttpReader reader, BrokerRequestHead head, Uri address, BrokerSession session, CancellationToken cancellationToken)
    {
        var host = SandboxSecretRules.NormalizeHost(address.IdnHost);

        if (session.IsBound(host))
        {
            await Answer(client, 403, $"A secret is bound to {host}, so the broker sends requests there only over https.", cancellationToken);
            return;
        }

        if (!session.MayReach(host) || !allowedPorts.Contains(address.Port))
        {
            await Answer(client, 403, $"This sandbox may not reach {host} on port {address.Port}.", cancellationToken);
            return;
        }

        Stream remote;

        try
        {
            remote = await upstream.Connect(host, address.Port, cancellationToken);
        }
        catch (Exception failure) when (failure is OutboundBlockedException or HttpRequestException or SocketException)
        {
            logger.LogDebug("Broker: {Failure}", failure.GetType().Name);
            await Answer(client, 502, $"{host} could not be reached.", cancellationToken);
            return;
        }

        await using (remote)
        {
            var lines = new StringBuilder($"{head.Method} {address.PathAndQuery} {head.Version}\r\nHost: {address.Authority}\r\n");

            foreach (var (name, value) in head.Headers.Where(header => !IsProxyHeader(header.Key) && !string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase) && !string.Equals(header.Key, "Connection", StringComparison.OrdinalIgnoreCase)))
            {
                lines.Append($"{name}: {value}\r\n");
            }

            lines.Append("Connection: close\r\n\r\n");
            await Write(remote, lines.ToString(), cancellationToken);
            await remote.WriteAsync(reader.Remaining(), cancellationToken);
            var bytes = await Pump(client, remote, session.SandboxId, cancellationToken);
            Log(session, host, $"plain request relayed, {bytes} bytes");
        }
    }

    #endregion

    #region Bound Hosts

    private async Task Intercept(Stream client, string host, int port, BrokerSession session, CancellationToken cancellationToken)
    {
        if (await broker.HostCertificate(session, host, cancellationToken) is not { } certificate)
        {
            await Answer(client, 502, "This space has no broker authority yet. Start the sandbox again.", cancellationToken);
            return;
        }

        await Write(client, "HTTP/1.1 200 Connection established\r\n\r\n", cancellationToken);
        await using var tls = new SslStream(client, leaveInnerStreamOpen: true);

        try
        {
            await tls.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    ApplicationProtocols = [SslApplicationProtocol.Http11],
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                },
                cancellationToken);
        }
        catch (AuthenticationException untrusted)
        {
            logger.LogDebug("Broker: {Failure}", untrusted.GetType().Name);
            // The sandbox's tool does not trust the space's authority. Nothing was sent anywhere.
            Log(session, host, "the sandbox did not trust the broker authority");
            return;
        }

        if (!string.IsNullOrEmpty(tls.TargetHostName) && !string.Equals(SandboxSecretRules.NormalizeHost(tls.TargetHostName), host, StringComparison.Ordinal))
        {
            Log(session, host, "the name asked for in TLS differs from the tunnel's host");
            return;
        }

        var reader = new BrokerHttpReader(tls);
        var bound = await broker.ValuesBoundTo(session, host, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            BrokerRequestHead? head;

            try
            {
                using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                waiting.CancelAfter(idle);
                head = await reader.ReadHead(waiting.Token);
            }
            catch (BrokerHttpException failure)
            {
                logger.LogDebug("Broker: {Failure}", failure.GetType().Name);
                await Answer(tls, 400, failure.Message, cancellationToken);
                return;
            }
            catch (Exception ended) when (ended is OperationCanceledException or IOException)
            {
                logger.LogDebug("Broker: {Failure}", ended.GetType().Name);
                return;
            }

            if (head is null || !await Forward(tls, reader, head, host, port, session, bound, cancellationToken) || WantsClose(head))
            {
                return;
            }
        }
    }

    /// <summary>Sends one request to a bound host with its placeholders replaced. False when the connection must close.</summary>
    private async Task<bool> Forward(Stream tls, BrokerHttpReader reader, BrokerRequestHead head, string host, int port, BrokerSession session, IReadOnlyList<string> bound, CancellationToken cancellationToken)
    {
        if (head.Has("Upgrade"))
        {
            await Answer(tls, 501, "The broker does not carry upgraded connections to a host a secret is bound to.", cancellationToken);
            return false;
        }

        if (head.Only("Host") is { } named && !string.Equals(SandboxSecretRules.NormalizeHost(named.Split(':')[0]), host, StringComparison.Ordinal))
        {
            await Answer(tls, 421, "This connection is for another host.", cancellationToken);
            return false;
        }

        if (head.Only("Expect") is { } expect && string.Equals(expect, "100-continue", StringComparison.OrdinalIgnoreCase))
        {
            await Write(tls, "HTTP/1.1 100 Continue\r\n\r\n", cancellationToken);
        }

        byte[] body;

        try
        {
            body = await reader.ReadBody(head, MaxBodyBytes, cancellationToken);
        }
        catch (BrokerTooLargeException failure)
        {
            logger.LogDebug("Broker: {Failure}", failure.GetType().Name);
            await Answer(tls, 413, failure.Message, cancellationToken);
            return false;
        }
        catch (BrokerHttpException failure)
        {
            logger.LogDebug("Broker: {Failure}", failure.GetType().Name);
            await Answer(tls, 400, failure.Message, cancellationToken);
            return false;
        }

        var holdsPlaceholder = body.AsSpan().IndexOf(Encoding.ASCII.GetBytes(BrokerTokens.PlaceholderPrefix)) >= 0;

        if (holdsPlaceholder && body.Length > ScrubbedBodyBytes)
        {
            await Answer(tls, 413, "A body that holds a placeholder may be at most 1 MB.", cancellationToken);
            return false;
        }

        var bodyText = holdsPlaceholder ? Encoding.UTF8.GetString(body) : string.Empty;
        var found = Placeholders(head, bodyText);

        if (!limits.Spend(session.SandboxId, body.Length))
        {
            await Answer(tls, 429, "This sandbox has moved as much through the broker this hour as it may.", cancellationToken);
            return false;
        }

        var substitution = await broker.Substitute(session, host, found, cancellationToken);

        if (substitution.Refusal is { } refusal)
        {
            Log(session, host, "a placeholder was refused");
            await Answer(tls, 403, refusal, cancellationToken);
            return true;
        }

        using var request = BuildRequest(head, host, port, body, bodyText, substitution.Values);
        HttpResponseMessage response;

        try
        {
            response = await upstream.Client.SendAsync(request, cancellationToken);
        }
        catch (Exception failure) when (failure is OutboundBlockedException or HttpRequestException)
        {
            logger.LogDebug("Broker: {Failure}", failure.GetType().Name);
            Log(session, host, "could not be reached");
            await Answer(tls, 502, $"{host} could not be reached.", cancellationToken);
            return false;
        }

        using (response)
        {
            if (response.Content.Headers.ContentEncoding.Any(coding => !string.Equals(coding, "identity", StringComparison.OrdinalIgnoreCase)))
            {
                // A compressed answer cannot be checked for the values, so it is not passed on.
                Log(session, host, "answered compressed after being asked not to");
                await Answer(tls, 502, $"{host} answered compressed, so the broker could not check it for secret values.", cancellationToken);
                return false;
            }

            var scrubber = new SecretScrubber([.. bound, .. substitution.Values.Values], Redactor.Mark, bytes: true);
            await WriteResponse(tls, head, response, scrubber, session.SandboxId, cancellationToken);
            Log(session, host, $"request answered {(int)response.StatusCode}");
        }

        return true;
    }

    private static List<(string Placeholder, BrokerPlacement Placement)> Placeholders(BrokerRequestHead head, string bodyText)
    {
        // The address, path and query alike, is read as written and unescaped, so an escaped placeholder is found too.
        var address = $"{head.Target} {Uri.UnescapeDataString(head.Target)}";
        return
        [
            .. head.Headers.SelectMany(header => BrokerTokens.FindPlaceholders(header.Value)).Select(found => (found, BrokerPlacement.Header)),
            .. BrokerTokens.FindPlaceholders(address).Select(found => (found, BrokerPlacement.Query)),
            .. BrokerTokens.FindPlaceholders(bodyText).Select(found => (found, BrokerPlacement.Body)),
        ];
    }

    private static HttpRequestMessage BuildRequest(
        BrokerRequestHead head,
        string host,
        int port,
        byte[] body,
        string bodyText,
        IReadOnlyDictionary<string, string> values)
    {
        var target = Replace(head.Target, values, escape: true);
        var request = new HttpRequestMessage(new HttpMethod(head.Method), new Uri($"https://{host}:{port}{(target.StartsWith('/') ? target : $"/{target}")}"))
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        var content = body.Length == 0
            ? null
            : new ByteArrayContent(values.Count > 0 && bodyText.Length > 0 ? Encoding.UTF8.GetBytes(Replace(bodyText, values, escape: false)) : body);
        request.Content = content;

        // The answer must come back whole and uncompressed, so every value in it can be found.
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");

        foreach (var (name, value) in head.Headers.Where(header => !hopByHop.Contains(header.Key.ToLowerInvariant())))
        {
            var replaced = Replace(value, values, escape: false);

            if (!request.Headers.TryAddWithoutValidation(name, replaced))
            {
                content?.Headers.TryAddWithoutValidation(name, replaced);
            }
        }

        return request;
    }

    private static string Replace(string text, IReadOnlyDictionary<string, string> values, bool escape)
    {
        foreach (var (placeholder, value) in values)
        {
            text = text.Replace(placeholder, escape ? Uri.EscapeDataString(value) : value, StringComparison.Ordinal);
        }

        return text;
    }

    private async Task WriteResponse(Stream tls, BrokerRequestHead head, HttpResponseMessage response, SecretScrubber scrubber, Guid sandboxId, CancellationToken cancellationToken)
    {
        var reason = scrubber.Scrub(Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(response.ReasonPhrase ?? "Status")));
        var lines = new StringBuilder($"HTTP/1.1 {(int)response.StatusCode} {Clean(reason)}\r\n");
        var noBody = head.Method is "HEAD" || (int)response.StatusCode is 204 or 304 || (int)response.StatusCode < 200;

        foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
        {
            if (hopByHop.Contains(name.ToLowerInvariant()) && !(noBody && string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var value in values)
            {
                lines.Append($"{name}: {Clean(scrubber.Scrub(Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(value))))}\r\n");
            }
        }

        if (noBody)
        {
            await WriteLatin1(tls, $"{lines}\r\n", cancellationToken);
            return;
        }

        lines.Append("Transfer-Encoding: chunked\r\n\r\n");
        await WriteLatin1(tls, lines.ToString(), cancellationToken);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);

        // Every body is scrubbed, whatever its type: read as one character a byte, binary passes through unchanged
        // except where the bytes are a value's.
        var stream = scrubber.Stream();
        var buffer = new byte[16 * 1024];
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (!limits.Spend(sandboxId, read))
            {
                return;
            }

            await WriteChunk(tls, Encoding.Latin1.GetBytes(stream.Add(Encoding.Latin1.GetString(buffer, 0, read))), cancellationToken);
        }

        await WriteChunk(tls, Encoding.Latin1.GetBytes(stream.Flush()), cancellationToken);
        await Write(tls, "0\r\n\r\n", cancellationToken);
    }

    private static string Clean(string value) =>
        value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);

    private static async Task WriteLatin1(Stream stream, string text, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(Encoding.Latin1.GetBytes(text), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    #endregion

    #region Private Methods

    private static bool IsProxyHeader(string name) => name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase);

    private static bool WantsClose(BrokerRequestHead head) =>
        head.Version is "HTTP/1.0" || head.All("Connection").Any(value => value.Contains("close", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads <c>host:port</c>, the target of a CONNECT. A host is a name or an address; a name is lowercased.</summary>
    private static bool TryReadAuthority(string target, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (!Uri.TryCreate($"https://{target}/", UriKind.Absolute, out var parsed) || parsed.UserInfo.Length > 0 || target.EndsWith(':') || !target.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        host = SandboxSecretRules.NormalizeHost(parsed.HostNameType is UriHostNameType.IPv6 ? parsed.Host.Trim('[', ']') : parsed.IdnHost);
        port = parsed.Port;
        return host.Length > 0 && port is > 0 and < 65536;
    }

    private async Task<long> Pump(Stream client, Stream remote, Guid sandboxId, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var up = Copy(client, remote, sandboxId, stop);
        var down = Copy(remote, client, sandboxId, stop);
        var first = await Task.WhenAny(up, down);
        await stop.CancelAsync();
        var bytes = await first;

        try
        {
            bytes += await (first == up ? down : up);
        }
        catch (Exception ended) when (ended is OperationCanceledException or IOException)
        {
            logger.LogDebug("Broker: {Failure}", ended.GetType().Name);
        }

        return bytes;
    }

    private async Task<long> Copy(Stream from, Stream to, Guid sandboxId, CancellationTokenSource stop)
    {
        var buffer = new byte[16 * 1024];
        long total = 0;

        try
        {
            while (true)
            {
                using var waiting = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                waiting.CancelAfter(idle);
                var read = await from.ReadAsync(buffer, waiting.Token);

                if (read == 0 || !limits.Spend(sandboxId, read))
                {
                    return total;
                }

                await to.WriteAsync(buffer.AsMemory(0, read), stop.Token);
                total += read;
            }
        }
        catch (Exception ended) when (ended is OperationCanceledException or IOException or SocketException)
        {
            logger.LogDebug("Broker: {Failure}", ended.GetType().Name);
            return total;
        }
    }

    private static Task Answer(Stream stream, int status, string sentence, CancellationToken cancellationToken, params (string Name, string Value)[] headers)
    {
        var body = Encoding.UTF8.GetBytes(sentence);
        var extra = string.Concat(headers.Select(header => $"{header.Name}: {header.Value}\r\n"));
        var reason = status switch
        {
            400 => "Bad Request",
            403 => "Forbidden",
            405 => "Method Not Allowed",
            407 => "Proxy Authentication Required",
            413 => "Content Too Large",
            421 => "Misdirected Request",
            429 => "Too Many Requests",
            501 => "Not Implemented",
            _ => "Bad Gateway",
        };
        return Write(stream, $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\n{extra}Connection: close\r\n\r\n{sentence}", cancellationToken);
    }

    private static async Task Write(Stream stream, string text, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task WriteChunk(Stream stream, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        await stream.WriteAsync(Encoding.ASCII.GetBytes($"{bytes.Length:x}\r\n"), cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.WriteAsync("\r\n"u8.ToArray(), cancellationToken);
    }

    private void Log(BrokerSession session, string host, string outcome) =>
        logger.LogInformation("Broker: sandbox {SandboxId} to {Host}: {Outcome}", session.SandboxId, host, outcome);

    #endregion
}
