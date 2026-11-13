namespace Drover.Server.Accounts;

/// <summary>
/// Prints where to finish setup and the setup code to the log, while the server has no users. The address is the
/// public one when it is set, otherwise each address the server listens on, with a wildcard host read as this
/// machine. It is printed once the server listens, so the addresses are the real ones.
/// </summary>
public sealed class SetupCodeAnnouncer(
    IServiceScopeFactory scopes,
    SetupCode code,
    IServer server,
    IHostApplicationLifetime lifetime,
    IConfiguration configuration,
    ILogger<SetupCodeAnnouncer> logger) : IHostedService
{
    #region Interface Implementations

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();

        if (await accounts.IsSetupOpen(cancellationToken))
        {
            lifetime.ApplicationStarted.Register(Announce);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    #endregion

    #region Public Methods

    /// <summary>Each address a person can open, given where the server listens: a wildcard host becomes <c>localhost</c>.</summary>
    public static IReadOnlyList<string> SetupAddresses(Uri? publicUrl, IEnumerable<string> listening)
    {
        if (publicUrl is not null)
        {
            return [$"{publicUrl.GetLeftPart(UriPartial.Authority)}/setup"];
        }

        return
        [
            .. listening
                .Select(address => address.Replace("://+:", "://localhost:", StringComparison.Ordinal)
                    .Replace("://*:", "://localhost:", StringComparison.Ordinal)
                    .Replace("://[::]:", "://localhost:", StringComparison.Ordinal)
                    .Replace("://0.0.0.0:", "://localhost:", StringComparison.Ordinal))
                .Select(address => $"{address.TrimEnd('/')}/setup")
                .Distinct(StringComparer.Ordinal),
        ];
    }

    #endregion

    #region Private Methods

    private void Announce()
    {
        var listening = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var addresses = SetupAddresses(PublicAddress.Read(configuration), listening);

        logger.LogWarning(
            "First-run setup is open. Open {SetupAddress} and enter the setup code {SetupCode}",
            addresses.Count == 0 ? "/setup on this server" : string.Join(" or ", addresses),
            code.Display);
    }

    #endregion
}
