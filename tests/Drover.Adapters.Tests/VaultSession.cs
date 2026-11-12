namespace Drover.Adapters.Tests;

/// <summary>One dependency injection scope with its tenant set and its tenant transaction open.</summary>
public sealed class VaultSession : IVaultSession
{
    #region Private Fields

    private readonly AsyncServiceScope scope;

    private readonly TenantWork work;

    #endregion

    #region Constructors

    private VaultSession(AsyncServiceScope scope, TenantWork work)
    {
        this.scope = scope;
        this.work = work;
    }

    #endregion

    #region Properties

    public ISecretVault Vault => scope.ServiceProvider.GetRequiredService<ISecretVault>();

    public DroverDbContext Db => scope.ServiceProvider.GetRequiredService<DroverDbContext>();

    #endregion

    #region Public Methods

    public static async Task<VaultSession> Open(IServiceProvider provider, Guid spaceId, Guid userId)
    {
        var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin();
        return new VaultSession(scope, work);
    }

    public Task Commit() => work.Commit();

    public async Task<byte[]> ReadStored(Guid secretId) =>
        await Db.Secrets.AsNoTracking().Where(secret => secret.Id == secretId).Select(secret => secret.Ciphertext).SingleAsync();

    public async Task WriteStored(Guid secretId, byte[] stored) =>
        await Db.Secrets.Where(secret => secret.Id == secretId).ExecuteUpdateAsync(
            update => update.SetProperty(secret => secret.Ciphertext, stored));

    public async ValueTask DisposeAsync()
    {
        await work.DisposeAsync();
        await scope.DisposeAsync();
    }

    #endregion
}
