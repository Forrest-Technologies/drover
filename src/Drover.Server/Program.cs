var initializeDatabase = args is ["--initialize-database"];
if (args.Contains("--initialize-database") && !initializeDatabase)
{
    throw new InvalidOperationException("Database initialization must run as a standalone command.");
}

var resetPassword = args is [PasswordResetCommand.Argument, var resetEmail] ? resetEmail : null;
if (args.Contains(PasswordResetCommand.Argument) && resetPassword is null)
{
    throw new InvalidOperationException($"Usage: {PasswordResetCommand.Argument} person@example.com, as a standalone command.");
}

var builder = WebApplication.CreateBuilder(initializeDatabase || resetPassword is not null ? [] : args);
if (initializeDatabase)
{
    await DatabaseStartup.Initialize(builder.Configuration);
    return;
}

if (resetPassword is not null)
{
    Environment.ExitCode = await PasswordResetCommand.Run(builder.Configuration, resetPassword);
    return;
}

// The web host writes every request line to the log, query string included. A tool server's sign-in page sends the
// person's browser back with a one-time code in the query, so request lines are not logged. Failures still are.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

var policy = new DeploymentPolicySetup(builder.Configuration).Resolve();
var connectionString = string.Empty;
try
{
    connectionString = DatabaseStartup.ApplicationConnection(builder.Configuration, policy.Rules.AllowMigrationCredentialInServer);
}
catch (Exception exception) when (exception is InvalidOperationException or ArgumentException
    or IOException or UnauthorizedAccessException)
{
    using var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
    var logger = loggerFactory.CreateLogger("Drover.Startup");
    logger.LogCritical("Database configuration is invalid. Configure a valid application database connection and its credentials.");
    Environment.ExitCode = 1;
    return;
}
var keyRingPath = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "drover", "keys");

builder.Services.Configure<DatabaseOptions>(options => options.ConnectionString = connectionString);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddOpenApi();
builder.Services.AddDroverData(connectionString);
builder.Services.AddDroverEngine();
builder.Services.AddDroverPolicy(builder.Configuration);
builder.Services.AddDroverModels();
builder.Services.AddDroverRunEngine(builder.Configuration);
builder.Services.AddDroverSecretVault(keyRingPath, builder.Configuration["DataProtection:CertificatePath"], builder.Configuration["DataProtection:CertificateKeyPath"]);
builder.Services.AddDroverSecurity(builder.Configuration);
builder.Services.AddSingleton(new KeyRingLocation(keyRingPath));
builder.Services.AddSingleton<SetupCode>();
builder.Services.AddHostedService<SetupCodeAnnouncer>();

var app = builder.Build();

app.UseTrustedProxies();
app.UseSecurityHeaders();
app.UseCookiePolicy();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<SpaceMembershipMiddleware>();

var api = app.MapGroup("/api").AddEndpointFilter<AntiforgeryFilter>();
api.MapAccountEndpoints();
api.MapRegistrationEndpoints();
api.MapPolicyEndpoints();
api.MapSpaceEndpoints();
api.MapFallback(() => Results.NotFound());
app.MapToolSignInCallback();

app.MapOpenApi();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapFallbackToFile("index.html");

await app.RunAsync();

public partial class Program;
