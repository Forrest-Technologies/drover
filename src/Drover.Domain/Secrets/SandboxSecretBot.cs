namespace Drover.Domain.Secrets;

/// <summary>
/// A bot a <see cref="SandboxSecretScope.Bots"/> secret is for. It carries the secret's name, so one bot cannot be
/// given two values under the same name.
/// </summary>
public sealed class SandboxSecretBot : ISpaceOwned
{
    #region Properties

    public Guid SpaceId { get; set; }

    public Guid SandboxSecretId { get; set; }

    public Guid BotId { get; set; }

    public required string Name { get; set; }

    #endregion
}
