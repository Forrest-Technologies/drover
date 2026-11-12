namespace Drover.Server.Tests.Support;

/// <summary>
/// The real server in process over a database of its own. It captures every log entry, and lets a request say which
/// address it came from (the in-process server has none) by sending <c>X-Test-Remote-Ip</c>.
/// </summary>
public sealed class DroverFactory(
    TestDatabase database,
    string keyRing,
    LogCapture logs,
    AdjustableTimeProvider time,
    HashCounter hashes,
    IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<Program>
{
    #region Constants

    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    #endregion

    #region Overrides

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Drover", database.AppConnectionString);
        builder.UseSetting("DataProtection:KeyPath", keyRing);

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(logs);
        });
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, RemoteAddressFilter>();
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton(hashes);
            services.AddSingleton<IPasswordHasher<User>, CountingPasswordHasher>();
        });
    }

    #endregion

    #region Nested Types

    private sealed class RemoteAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(
            Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, pipeline) =>
                {
                    if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var address))
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
                    }

                    await pipeline();
                });
                next(app);
            };
    }

    #endregion
}
