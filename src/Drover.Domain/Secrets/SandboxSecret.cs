namespace Drover.Domain.Secrets;

/// <summary>
/// A credential a person gives bots for their sandboxes, as an environment variable. The value is in the vault
/// (<see cref="SecretId"/>); this row holds only what may be shown: the name, where the value may be sent, and who
/// may use it.
/// </summary>
public sealed class SandboxSecret : ISpaceOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    /// <summary>The environment variable name, as <see cref="SandboxSecretRules.NamePattern"/> allows.</summary>
    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    public SandboxSecretMode Mode { get; set; } = SandboxSecretMode.Brokered;

    /// <summary>The host names a brokered value may be sent to, lowercased; <c>*.example.com</c> covers subdomains.</summary>
    public List<string> Hosts { get; set; } = [];

    /// <summary>Whether the broker may put the value in a query parameter as well as a request header.</summary>
    public bool AllowInQuery { get; set; }

    /// <summary>Whether the broker may put the value in a request body as well as a request header.</summary>
    public bool AllowInBody { get; set; }

    public SandboxSecretScope Scope { get; set; } = SandboxSecretScope.Bots;

    /// <summary>The project, for <see cref="SandboxSecretScope.Project"/>.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>The value in the vault. A plain column, not a foreign key: the secret has an audience and this row has none.</summary>
    public Guid SecretId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? RotatedByUserId { get; set; }

    public DateTimeOffset? RotatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    #endregion

    #region Public Methods

    public override string ToString() => $"{nameof(SandboxSecret)} {Name}";

    #endregion
}
