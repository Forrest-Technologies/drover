namespace Drover.Domain.Models;

/// <summary>
/// A way to reach models: a kind, an address, a way to authenticate. <see cref="Kind"/> is a string, not an enum,
/// because each adapter project names its own (<c>openai_compatible</c>, <c>venice</c>, ...).
/// </summary>
public sealed class ModelConnection : ISpaceOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public required string Name { get; set; }

    public required string Kind { get; set; }

    public string? BaseUrl { get; set; }

    public ModelAuthKind AuthKind { get; set; } = ModelAuthKind.ApiKey;

    public CredentialScope CredentialScope { get; set; } = CredentialScope.Space;

    /// <summary>The key in the vault. A plain column, not a foreign key: the secret has an audience and this row has none.</summary>
    public Guid? SecretId { get; set; }

    public string? DefaultModel { get; set; }

    public ConnectionStatus Status { get; set; } = ConnectionStatus.Pending;

    public ConnectionBalance? Balance { get; set; }

    public DateTimeOffset? LastCheckedAt { get; set; }
}
