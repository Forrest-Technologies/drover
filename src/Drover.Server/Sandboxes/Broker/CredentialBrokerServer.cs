namespace Drover.Server.Sandboxes.Broker;

/// <summary>
/// The credential broker's listener: TLS on its own port, in front of <see cref="BrokerConnection"/>. It answers
/// proxy requests from sandboxes and nothing else: there is no application route on this port. Only registered when
/// the operator set the broker up.
/// </summary>
public sealed class CredentialBrokerServer(
    IOptions<CredentialBrokerOptions> options,
    CredentialBrokerService broker,
    BrokerUpstream upstream,
    BrokerLimits limits,
    ILogger<CredentialBrokerServer> logger) : BackgroundService
{
    #region Private Fields

    private const int MaxConnections = 1024;

    private readonly SemaphoreSlim connections = new(MaxConnections);

    private TcpListener? listener;

    #endregion

    #region Properties

    /// <summary>The port it listens on once started, which is the configured one unless that was zero.</summary>
    public int Port => (listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;

    /// <summary>Set when it is listening, for a caller that waits on it.</summary>
    public TaskCompletionSource Listening { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    #endregion

    #region Public Methods

    /// <summary>The listener's certificate, read from the operator's PEM files. A missing or unreadable file stops the server, naming the setting.</summary>
    public static X509Certificate2 ReadCertificate(CredentialBrokerOptions options)
    {
        try
        {
            using var read = X509Certificate2.CreateFromPemFile(options.CertificatePath!, options.CertificateKeyPath);
            return X509CertificateLoader.LoadPkcs12(read.Export(X509ContentType.Pkcs12), null);
        }
        catch (Exception failure) when (failure is IOException or CryptographicException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"{CredentialBrokerOptions.Section}:CertificatePath does not name a readable PEM certificate with its key. Correct the setting, or remove the broker settings to turn the broker off.");
        }
    }

    #endregion

    #region Protected Methods

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var certificate = ReadCertificate(options.Value);
        listener = new TcpListener(IPAddress.IPv6Any, options.Value.ListenPort);
        listener.Server.DualMode = true;
        listener.Start();
        Listening.TrySetResult();
        logger.LogInformation("The credential broker listens on port {Port}", Port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var accepted = await listener.AcceptTcpClientAsync(stoppingToken);

                if (!await connections.WaitAsync(TimeSpan.Zero, stoppingToken))
                {
                    accepted.Dispose();
                    continue;
                }

                _ = Handle(accepted, certificate, stoppingToken);
            }
        }
        catch (OperationCanceledException stopped) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("The credential broker stopped: {Reason}", stopped.GetType().Name);
        }
        finally
        {
            listener.Stop();
        }
    }

    #endregion

    #region Private Methods

    private async Task Handle(TcpClient accepted, X509Certificate2 certificate, CancellationToken stoppingToken)
    {
        try
        {
            using (accepted)
            {
                accepted.NoDelay = true;
                var remote = (accepted.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
                await using var tls = new SslStream(accepted.GetStream(), leaveInnerStreamOpen: false);
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                handshake.CancelAfter(TimeSpan.FromSeconds(10));
                await tls.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = certificate,
                        ApplicationProtocols = [SslApplicationProtocol.Http11],
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    },
                    handshake.Token);
                await new BrokerConnection(broker, upstream, limits, options.Value.AllowedPorts, logger).Serve(tls, remote, stoppingToken);
            }
        }
        catch (Exception ended) when (ended is IOException or AuthenticationException or OperationCanceledException or SocketException or ObjectDisposedException)
        {
            logger.LogDebug("A broker connection ended: {Failure}", ended.GetType().Name);
        }
        catch (Exception failure)
        {
            logger.LogWarning("A broker connection failed: {Failure}", failure.GetType().Name);
        }
        finally
        {
            connections.Release();
        }
    }

    #endregion
}
