namespace Drover.Server.Accounts;

/// <summary>Prints the setup code to the log at start, while the server has no users.</summary>
public sealed class SetupCodeAnnouncer(
    IServiceScopeFactory scopes,
    SetupCode code,
    ILogger<SetupCodeAnnouncer> logger) : IHostedService
{
    #region Interface Implementations

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();

        if (await accounts.IsSetupOpen(cancellationToken))
        {
            logger.LogWarning(
                "First-run setup is open. Setup code for requests from another machine: {SetupCode}",
                code.Display);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    #endregion
}
