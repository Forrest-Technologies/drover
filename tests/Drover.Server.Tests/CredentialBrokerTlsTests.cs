namespace Drover.Server.Tests;

/// <summary>
/// The credential broker end to end over real sockets and TLS: a sandbox started with a brokered secret reaches the
/// broker with its token, opens a tunnel to the host the secret is bound to, trusts the space's authority, and sends
/// its placeholder; the host receives the real value, and what returns to the sandbox has the value scrubbed. A wrong
/// token, a host the bot's network does not allow, and plain HTTP to a bound host are refused. Every value, key and
/// certificate is generated for the test.
/// </summary>
public sealed class CredentialBrokerTlsTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string BoundHost = "api.example.test";

    private static readonly Lazy<(string Certificate, string Key)> listenerFiles = new(WriteListenerCertificate);

    private readonly MemorySandboxProvider provider = new();

    private readonly FakeHost upstream = new(BoundHost);

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));
        services.AddSingleton<ISandboxProvider>(provider);
        services.AddSandboxTools();
        services.Configure<CredentialBrokerOptions>(options =>
        {
            options.PublicHost = "broker.example.test";
            options.ListenPort = 0;
            options.CertificatePath = listenerFiles.Value.Certificate;
            options.CertificateKeyPath = listenerFiles.Value.Key;
        });
        services.AddSingleton(upstream.Upstream());
        services.AddSingleton<BrokerLimits>();
        services.AddSingleton<CredentialBrokerServer>();
    }

    #endregion

    #region Exchanges

    [Fact]
    public async Task A_placeholder_reaches_its_bound_host_as_the_real_value_and_the_value_does_not_come_back()
    {
        var value = $"test-{Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()}";
        var (spec, authority) = await StartSandboxWithSecret(value);
        var broker = await StartBroker();
        var placeholder = spec.Environment["DEPLOY_TOKEN"];

        await using var tunnel = await OpenTunnel(broker.Port, TokenOf(spec), $"{BoundHost}:443");
        await using var tls = await TrustingOnly(tunnel.Stream, authority);
        await Send(tls, $"GET /v1/deploy?mode=fast HTTP/1.1\r\nHost: {BoundHost}\r\nAuthorization: Bearer {placeholder}\r\nConnection: close\r\n\r\n");
        var answer = await ReadAll(tls);

        upstream.Received.Single().Only("Authorization").Should().Be($"Bearer {value}");
        upstream.Received.Single().Target.Should().Be("/v1/deploy?mode=fast");
        answer.Should().StartWith("HTTP/1.1 200").And.Contain(Redactor.Mark).And.NotContain(value);
        Logs.AllText().Should().NotContain(value).And.NotContain(placeholder).And.NotContain(TokenOf(spec));
        await upstream.DisposeAsync();
    }

    [Fact]
    public async Task A_value_a_bound_host_kept_is_scrubbed_from_a_later_answer_with_no_placeholder_in_any_type_of_body()
    {
        var value = $"test-{Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()}";
        var (spec, authority) = await StartSandboxWithSecret(value);
        var broker = await StartBroker();
        var placeholder = spec.Environment["DEPLOY_TOKEN"];

        await using (var first = await OpenTunnel(broker.Port, TokenOf(spec), $"{BoundHost}:443"))
        {
            await using var tls = await TrustingOnly(first.Stream, authority);
            await Send(tls, $"PUT /store HTTP/1.1\r\nHost: {BoundHost}\r\nAuthorization: Bearer {placeholder}\r\nConnection: close\r\n\r\n");
            await ReadAll(tls);
        }

        await using var second = await OpenTunnel(broker.Port, TokenOf(spec), $"{BoundHost}:443");
        await using var again = await TrustingOnly(second.Stream, authority);
        await Send(again, $"GET /store?as=binary HTTP/1.1\r\nHost: {BoundHost}\r\nConnection: close\r\n\r\n");
        var answer = await ReadAll(again);

        answer.Should().StartWith("HTTP/1.1 200").And.Contain(Redactor.Mark).And.NotContain(value);
        upstream.Received.Last().Only("Accept-Encoding").Should().Be("identity");
        await upstream.DisposeAsync();
    }

    [Fact]
    public async Task A_wrong_token_a_host_the_network_forbids_and_plain_http_to_a_bound_host_are_refused()
    {
        var (spec, _) = await StartSandboxWithSecret($"test-{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}");
        var broker = await StartBroker();

        await using (var stranger = await OpenTunnel(broker.Port, BrokerTokens.NewToken(), $"{BoundHost}:443", expectOk: false))
        {
            stranger.Status.Should().StartWith("HTTP/1.1 407");
        }

        await using (var forbidden = await OpenTunnel(broker.Port, TokenOf(spec), "collector.example.test:443", expectOk: false))
        {
            forbidden.Status.Should().StartWith("HTTP/1.1 403");
        }

        await using (var otherPort = await OpenTunnel(broker.Port, TokenOf(spec), $"{BoundHost}:8443", expectOk: false))
        {
            otherPort.Status.Should().StartWith("HTTP/1.1 403");
        }

        await using var plain = await Connect(broker.Port);
        await Send(plain, $"GET http://{BoundHost}/v1 HTTP/1.1\r\nHost: {BoundHost}\r\nProxy-Authorization: {Basic(TokenOf(spec))}\r\n\r\n");
        (await ReadAll(plain)).Should().StartWith("HTTP/1.1 403").And.Contain("only over https");
        upstream.Received.Should().BeEmpty();
        await upstream.DisposeAsync();
    }

    #endregion

    #region Helpers

    /// <summary>A bot with a sandbox and a secret bound to the fake host; a command starts the sandbox.</summary>
    private async Task<(SandboxSpec Spec, X509Certificate2 Authority)> StartSandboxWithSecret(string value)
    {
        var person = await SeedPerson("broker");
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        var space = $"/api/spaces/{person.SpaceId}";
        var connection = await (await client.Post($"{space}/model-connections", new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted")))
            .Content.ReadFromJsonAsync<ModelConnectionSummary>(Token);
        var bot = await (await client.Post($"{space}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection!.Id, null))).Content.ReadFromJsonAsync<BotSummary>(Token);
        (await client.Put($"{space}/bots/{bot!.Id}/definition", new UpdateBotRequest(null, null, null, null, null, null, null, null, null, "private", new SandboxSettingsBody("off", [], null), null, null)))
            .EnsureSuccessStatusCode();
        (await client.Post($"{space}/sandbox-secrets", new CreateSandboxSecretRequest("DEPLOY_TOKEN", null, value, "brokered", [BoundHost], false, false, "bots", [bot.Id], null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.Post($"{space}/bots/{bot.Id}/sandbox/commands", new SandboxCommandRequest("echo hi"))).EnsureSuccessStatusCode();
        var authority = await ReadAuthority(person.SpaceId);
        return (provider.Specs.Single(), X509Certificate2.CreateFromPem(authority));
    }

    private async Task<string> ReadAuthority(Guid spaceId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetSpace(spaceId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var row = await scope.ServiceProvider.GetRequiredService<DroverDbContext>().BrokerAuthorities.SingleAsync(Token);
        await work.Commit(Token);
        return row.Certificate;
    }

    private async Task<CredentialBrokerServer> StartBroker()
    {
        var broker = Services.GetRequiredService<CredentialBrokerServer>();
        await broker.StartAsync(Token);
        await broker.Listening.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        return broker;
    }

    private static string TokenOf(SandboxSpec spec) => new Uri(spec.Environment["HTTPS_PROXY"]).UserInfo.Split(':')[1];

    private static string Basic(string token) => $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{BrokerTokens.ProxyUser}:{token}"))}";

    private static async Task<SslStream> Connect(int port)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, Token);
        var tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, certificate, _, _) => certificate?.Subject == "CN=broker.example.test");
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "broker.example.test" }, Token);
        return tls;
    }

    private static async Task<Tunnel> OpenTunnel(int port, string token, string target, bool expectOk = true)
    {
        var stream = await Connect(port);
        await Send(stream, $"CONNECT {target} HTTP/1.1\r\nHost: {target}\r\nProxy-Authorization: {Basic(token)}\r\n\r\n");
        var status = await ReadHead(stream);

        if (expectOk)
        {
            status.Should().StartWith("HTTP/1.1 200");
        }

        return new Tunnel(stream, status);
    }

    /// <summary>TLS inside the tunnel, trusting only the space's broker authority.</summary>
    private static async Task<SslStream> TrustingOnly(Stream tunnel, X509Certificate2 authority)
    {
        var tls = new SslStream(tunnel, leaveInnerStreamOpen: true, (_, certificate, _, _) =>
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(authority);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return certificate is X509Certificate2 presented && chain.Build(presented);
        });
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = BoundHost }, Token);
        return tls;
    }

    private static Task Send(Stream stream, string text) => stream.WriteAsync(Encoding.UTF8.GetBytes(text), Token).AsTask();

    private static async Task<string> ReadHead(Stream stream)
    {
        var head = new List<byte>();
        var one = new byte[1];

        while (await stream.ReadAsync(one, Token) == 1)
        {
            head.Add(one[0]);

            if (head.Count >= 4 && Encoding.ASCII.GetString(CollectionsMarshal.AsSpan(head)[^4..]) == "\r\n\r\n")
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(head));
    }

    private static async Task<string> ReadAll(Stream stream)
    {
        using var all = new MemoryStream();

        try
        {
            await stream.CopyToAsync(all, Token).WaitAsync(TimeSpan.FromSeconds(15), Token);
        }
        catch (IOException ended)
        {
            // A peer that closes without a TLS goodbye ends the read this way; what arrived before it is the answer.
            return all.Length > 0 ? Encoding.UTF8.GetString(all.ToArray()) : throw new InvalidOperationException("The broker closed without an answer.", ended);
        }

        return Encoding.UTF8.GetString(all.ToArray());
    }

    private static (string Certificate, string Key) WriteListenerCertificate()
    {
        var directory = Directory.CreateTempSubdirectory("drover-broker-").FullName;
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=broker.example.test", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("broker.example.test");
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var certificatePath = Path.Combine(directory, "broker.pem");
        var keyPath = Path.Combine(directory, "broker.key");
        File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        return (certificatePath, keyPath);
    }

    #endregion

    #region Nested Types

    private sealed record Tunnel(SslStream Stream, string Status) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Stream.DisposeAsync();
    }

    /// <summary>
    /// A host a secret is bound to, on loopback, with its own throwaway certificate. It records each request and
    /// answers with the Authorization header it was given, as a careless service might.
    /// </summary>
    private sealed class FakeHost : IAsyncDisposable
    {
        #region Private Fields

        private readonly TcpListener listener = new(IPAddress.Loopback, 0);

        private readonly X509Certificate2 certificate;

        private readonly CancellationTokenSource stop = new();

        private string? stored;

        #endregion

        #region Constructors

        public FakeHost(string host)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName(host);
            request.CertificateExtensions.Add(names.Build());
            using var made = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            certificate = X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pkcs12), null);
            listener.Start();
            _ = Serve();
        }

        #endregion

        #region Properties

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public ConcurrentQueue<BrokerRequestHead> Received { get; } = new();

        #endregion

        #region Public Methods

        /// <summary>
        /// The broker's way out for these tests: every host is this one, on loopback, and its throwaway certificate is
        /// the one trusted. The production path refuses loopback and trusts the system's roots.
        /// </summary>
        public BrokerUpstream Upstream()
        {
            var trusted = certificate.Thumbprint;
#pragma warning disable RS0030 // A test double needs its own handler to reach loopback and trust a throwaway certificate.
            var handler = new SocketsHttpHandler
#pragma warning restore RS0030
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                UseCookies = false,
                ConnectCallback = async (_, cancellationToken) =>
                {
                    var tcp = new TcpClient();
                    await tcp.ConnectAsync(IPAddress.Loopback, Port, cancellationToken);
                    return tcp.GetStream();
                },
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, presented, _, _) => presented is X509Certificate2 found && found.Thumbprint == trusted,
                },
            };
            return new BrokerUpstream(
                async (_, _, cancellationToken) =>
                {
                    var tcp = new TcpClient();
                    await tcp.ConnectAsync(IPAddress.Loopback, Port, cancellationToken);
                    return tcp.GetStream();
                },
                new HttpMessageInvoker(handler, disposeHandler: true));
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            listener.Stop();
            certificate.Dispose();
        }

        #endregion

        #region Private Methods

        private async Task<Exception?> Serve()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var accepted = await listener.AcceptTcpClientAsync(stop.Token);
                    await using var tls = new SslStream(accepted.GetStream());
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, stop.Token);
                    var head = await new BrokerHttpReader(tls).ReadHead(stop.Token);

                    if (head is null)
                    {
                        continue;
                    }

                    Received.Enqueue(head);
                    stored = head.Only("Authorization") ?? stored;
                    var binary = head.Target.Contains("binary", StringComparison.Ordinal);
                    var body = binary ? $"\u0001\u0002{stored}\u0003" : JsonSerializer.Serialize(new { authorization = stored });
                    var type = binary ? "application/octet-stream" : "application/json";
                    await tls.WriteAsync(Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}"), stop.Token);
                }
            }
            catch (Exception stopped) when (stopped is OperationCanceledException or ObjectDisposedException or SocketException or IOException)
            {
                // The fake host stops when the test ends.
                return stopped;
            }

            return null;
        }

        #endregion
    }

    #endregion
}
