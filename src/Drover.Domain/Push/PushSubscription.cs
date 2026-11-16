namespace Drover.Domain.Push;

/// <summary>
/// One device of one person that may be woken: the push service's address for it and the keys its payload is
/// encrypted to. A device belongs to a person, not to a space, so the row is the person's alone in every space.
/// </summary>
public sealed class PushSubscription : IUserOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    /// <summary>The push service's address for this device. Unique: a device registered again replaces its row.</summary>
    public required string Endpoint { get; set; }

    /// <summary>The device's public key, base64url, that the payload is encrypted to.</summary>
    public required string PublicKey { get; set; }

    /// <summary>The device's authentication secret, base64url.</summary>
    public required string AuthSecret { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    #endregion
}
