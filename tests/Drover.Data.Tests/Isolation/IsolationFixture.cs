namespace Drover.Data.Tests.Isolation;

/// <summary>
/// The harnesses one test class shares: one over the real database, and one over a scratch database that holds a
/// correct parent and child with audiences. The real schema has no audience table yet (step 5 adds them), so the
/// scratch pair keeps the audience checks and the parent checks running, and proves they work, from now on.
/// </summary>
public sealed class IsolationFixture(PostgresFixture postgres) : IAsyncLifetime
{
    #region Private Fields

    private ScratchDatabase? scratch;

    private IsolationHarness? real;

    private IsolationHarness? pair;

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        real = await postgres.OpenHarness();
        scratch = await postgres.CreateScratchDatabase();
        await ScratchSchema.CreateAudiencePair(scratch, ChildKey.Correct);
        pair = await scratch.Harness(ScratchSchema.AudiencePair(OwnershipMap.Current), ScratchSchema.AllSeeders());
    }

    public async ValueTask DisposeAsync()
    {
        if (real is not null)
        {
            await real.DisposeAsync();
        }

        if (pair is not null)
        {
            await pair.DisposeAsync();
        }

        if (scratch is not null)
        {
            await scratch.DisposeAsync();
        }
    }

    #endregion

    #region Public Methods

    /// <summary>The harness that holds <paramref name="table"/>: the scratch one for a scratch table, else the real one.</summary>
    public IsolationHarness For(string table) =>
        (table.StartsWith(IsolationTables.ScratchPrefix, StringComparison.Ordinal) ? pair : real)!;

    #endregion
}
