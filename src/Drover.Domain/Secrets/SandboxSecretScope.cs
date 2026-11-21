namespace Drover.Domain.Secrets;

/// <summary>Which bots a sandbox secret is for. The person who saves it chooses.</summary>
public enum SandboxSecretScope
{
    /// <summary>Every bot of the space that has a sandbox.</summary>
    Space,

    /// <summary>Only the bots listed for it.</summary>
    Bots,

    /// <summary>Only work done in one project. Brokered secrets only.</summary>
    Project,
}
