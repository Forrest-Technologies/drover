var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Drover")
    ?? throw new InvalidOperationException("ConnectionStrings:Drover is not set. It names the drover_app database role.");
var keyRingPath = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "drover", "keys");

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddDroverData(connectionString);
builder.Services.AddDroverEngine();
builder.Services.AddDroverPolicy(builder.Configuration);
builder.Services.AddDroverSecretVault(keyRingPath, builder.Configuration["DataProtection:CertificatePath"], builder.Configuration["DataProtection:CertificateKeyPath"]);
builder.Services.AddDroverSecurity(builder.Configuration);
builder.Services.AddSingleton<SetupCode>();
builder.Services.AddHostedService<SetupCodeAnnouncer>();

var app = builder.Build();

app.UseSecurityHeaders();
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

app.MapOpenApi();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapFallbackToFile("index.html");

await app.RunAsync();

public partial class Program;
