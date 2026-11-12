namespace Drover.Adapters.Conformance.Secrets;

/// <summary>
/// One unit of work against a vault, as a service would run it: acting as one person in one space, with the
/// storage reachable for the tests that copy a stored value around.
/// </summary>
public interface IVaultSession : IAsyncDisposable
{
    ISecretVault Vault { get; }

    /// <summary>Ends the unit of work. Nothing the vault did is visible to another session before this.</summary>
    Task Commit();

    /// <summary>The bytes the adapter keeps for the secret, exactly as stored.</summary>
    Task<byte[]> ReadStored(Guid secretId);

    /// <summary>Replaces the bytes the adapter keeps for the secret, as an attacker with database access could.</summary>
    Task WriteStored(Guid secretId, byte[] stored);
}

/// <summary>
/// What every <see cref="ISecretVault"/> must do. An adapter inherits this class and supplies the three members
/// that create spaces, people, and sessions. A new behavior every adapter must have is added here first.
/// </summary>
public abstract class SecretVaultConformance
{
    #region Private Fields

    private const string Value = "conformance-value-7f3a91c2";

    #endregion

    #region Adapter Hooks

    protected abstract Task<Guid> CreateSpace();

    protected abstract Task<Guid> CreateMember(Guid spaceId);

    protected abstract Task<IVaultSession> OpenSession(Guid spaceId, Guid userId);

    #endregion

    #region Tests

    [Fact]
    public async Task Reveal_returns_what_was_stored()
    {
        var (space, user) = await NewSpaceWithMember();
        var id = await Run(space, user, session => session.Vault.Store(SecretKind.ModelKey, Value, null, Token));

        var revealed = await Run(space, user, session => session.Vault.Reveal(id, Token));

        revealed.Should().Be(Value);
    }

    [Fact]
    public async Task What_is_stored_does_not_contain_the_value()
    {
        var (space, user) = await NewSpaceWithMember();
        var id = await Run(space, user, session => session.Vault.Store(SecretKind.ModelKey, Value, null, Token));

        var stored = await Run(space, user, session => session.ReadStored(id));

        Encoding.UTF8.GetString(stored).Should().NotContain(Value);
    }

    [Fact]
    public async Task Replace_changes_the_value_and_keeps_the_id()
    {
        var (space, user) = await NewSpaceWithMember();
        var id = await Run(space, user, session => session.Vault.Store(SecretKind.ToolHeader, Value, null, Token));

        await Run(space, user, session => session.Vault.Replace(id, "replacement-value-5d1e", Token));

        (await Run(space, user, session => session.Vault.Reveal(id, Token))).Should().Be("replacement-value-5d1e");
    }

    [Fact]
    public async Task Delete_removes_the_secret()
    {
        var (space, user) = await NewSpaceWithMember();
        var id = await Run(space, user, session => session.Vault.Store(SecretKind.PushKey, Value, null, Token));

        await Run(space, user, session => session.Vault.Delete(id, Token));

        var reveal = () => Run(space, user, session => session.Vault.Reveal(id, Token));
        await reveal.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Reveal_of_an_unknown_id_throws()
    {
        var (space, user) = await NewSpaceWithMember();

        var reveal = () => Run(space, user, session => session.Vault.Reveal(Guid.CreateVersion7(), Token));

        await reveal.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task A_private_secret_is_invisible_to_another_member_of_the_space()
    {
        var (space, owner) = await NewSpaceWithMember();
        var other = await CreateMember(space);
        var id = await Run(space, owner, session => session.Vault.Store(SecretKind.ModelKey, Value, owner, Token));

        var reveal = () => Run(space, other, session => session.Vault.Reveal(id, Token));

        (await Run(space, owner, session => session.Vault.Reveal(id, Token))).Should().Be(Value);
        await reveal.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task A_ciphertext_copied_to_another_secret_does_not_decrypt()
    {
        var (space, user) = await NewSpaceWithMember();
        var original = await Run(space, user, session => session.Vault.Store(SecretKind.ModelKey, Value, null, Token));
        var target = await Run(space, user, session => session.Vault.Store(SecretKind.ModelKey, "other-value", null, Token));
        var stolen = await Run(space, user, session => session.ReadStored(original));

        await Run(space, user, session => session.WriteStored(target, stolen));

        var reveal = () => Run(space, user, session => session.Vault.Reveal(target, Token));
        await reveal.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task A_ciphertext_copied_to_another_spaces_row_does_not_decrypt()
    {
        var (spaceA, userA) = await NewSpaceWithMember();
        var (spaceB, userB) = await NewSpaceWithMember();
        var original = await Run(spaceA, userA, session => session.Vault.Store(SecretKind.ModelKey, Value, null, Token));
        var target = await Run(spaceB, userB, session => session.Vault.Store(SecretKind.ModelKey, "other-value", null, Token));
        var stolen = await Run(spaceA, userA, session => session.ReadStored(original));

        await Run(spaceB, userB, session => session.WriteStored(target, stolen));

        var reveal = () => Run(spaceB, userB, session => session.Vault.Reveal(target, Token));
        await reveal.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task A_space_cannot_reveal_another_spaces_secret_by_id()
    {
        var (spaceA, userA) = await NewSpaceWithMember();
        var (spaceB, userB) = await NewSpaceWithMember();
        var id = await Run(spaceA, userA, session => session.Vault.Store(SecretKind.ModelKey, Value, null, Token));

        var reveal = () => Run(spaceB, userB, session => session.Vault.Reveal(id, Token));

        await reveal.Should().ThrowAsync<KeyNotFoundException>();
    }

    #endregion

    #region Private Methods

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<(Guid Space, Guid User)> NewSpaceWithMember()
    {
        var space = await CreateSpace();
        return (space, await CreateMember(space));
    }

    private async Task<T> Run<T>(Guid space, Guid user, Func<IVaultSession, Task<T>> work)
    {
        await using var session = await OpenSession(space, user);
        var result = await work(session);
        await session.Commit();
        return result;
    }

    private async Task Run(Guid space, Guid user, Func<IVaultSession, Task> work)
    {
        await using var session = await OpenSession(space, user);
        await work(session);
        await session.Commit();
    }

    #endregion
}
