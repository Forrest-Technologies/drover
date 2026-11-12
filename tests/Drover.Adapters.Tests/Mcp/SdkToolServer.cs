namespace Drover.Adapters.Tests.Mcp;

/// <summary>
/// A tool server built on the protocol library's own server, listening on a loopback port, so the adapter is tested
/// over real HTTP against the real protocol of each generation.
/// </summary>
public sealed class SdkToolServer(ToolServerSpec spec) : IToolServer
{
    #region Private Fields

    private WebApplication? app;

    private int port;

    private int handshakes;

    #endregion

    #region Properties

    public Uri Url => new($"http://127.0.0.1:{port}/{(spec.Generation is ServerGeneration.LegacySse ? "sse" : string.Empty)}");

    public int Handshakes => Volatile.Read(ref handshakes);

    #endregion

    #region Public Methods

    public static async Task<SdkToolServer> Start(ToolServerSpec spec)
    {
        var server = new SdkToolServer(spec);
        await server.Run();
        return server;
    }

    #endregion

    #region Interface Implementations

    public async Task Restart()
    {
        await ShutDown();
        await Run();
    }

    public async Task ShutDown()
    {
        if (app is { } running)
        {
            app = null;
            await running.StopAsync(TestContext.Current.CancellationToken);
            await running.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync() => await ShutDown();

    #endregion

    #region Private Methods

    private async Task Run()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Logging.ClearProviders();
        builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.SessionMode = spec.Generation switch
                {
                    ServerGeneration.Stateless => HttpServerSessionMode.Stateless,
                    ServerGeneration.Both => HttpServerSessionMode.StatefulForInitializeClients,
                    _ => HttpServerSessionMode.Stateful,
                };

                // The oldest transport is obsolete in the library and still deployed. This test server offers it, on
                // loopback only, so the adapter's fall back to it is proven against the real thing.
#pragma warning disable MCP9004
                options.EnableLegacySse = spec.Generation is ServerGeneration.LegacySse;
#pragma warning restore MCP9004
            })
            .WithTools(Tools());
        var built = builder.Build();
        built.Use(CheckHeaderAndCountHandshakes);
        built.MapMcp();
        await built.StartAsync(TestContext.Current.CancellationToken);
        port = new Uri(built.Urls.First()).Port;
        app = built;
    }

    private async Task CheckHeaderAndCountHandshakes(HttpContext context, RequestDelegate next)
    {
        if (spec.HeaderName is { } name && context.Request.Headers[name] != spec.HeaderValue)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (HttpMethods.IsPost(context.Request.Method))
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;

            if (body.Contains("\"initialize\"", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref handshakes);
            }
        }

        await next(context);
    }

    private static IEnumerable<McpServerTool> Tools() =>
    [
        McpServerTool.Create(
            (string text) => $"echo: {text}",
            new McpServerToolCreateOptions { Name = "echo", Description = "Repeats the text it is given.", ReadOnly = true, Destructive = false }),
        McpServerTool.Create(
            () => new Recorded(true),
            new McpServerToolCreateOptions { Name = "record", Description = "Records something for good.", ReadOnly = false, Destructive = true, UseStructuredContent = true }),
        McpServerTool.Create(
            string () => throw new InvalidOperationException("The tool could not do it."),
            new McpServerToolCreateOptions { Name = "fail", Description = "Always reports an error." }),
        McpServerTool.Create(
            async (CancellationToken cancellationToken) =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return "never";
            },
            new McpServerToolCreateOptions { Name = "slow", Description = "Never answers." }),
    ];

    #endregion

    #region Nested Types

    private sealed record Recorded(bool recorded);

    #endregion
}
