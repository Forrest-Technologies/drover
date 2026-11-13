namespace Drover.Contracts.Status;

/// <summary>
/// The state of the server as a space's admins may see it. <see cref="Server"/> is null where the deployment keeps
/// the server's own state to its operator; a space's connections have their own lists.
/// </summary>
public sealed record SpaceStatus(DateTimeOffset CheckedAt, IReadOnlyList<StatusRow>? Server);

/// <summary>
/// One thing that was checked. <c>Name</c> is database, migrations, background_work, key_ring, or public_address.
/// <c>State</c> is ok, attention (works, and something should be looked at), failing, or off (optional and not set
/// up). <c>Summary</c> says what was found, and what to do about it when there is something to do.
/// </summary>
public sealed record StatusRow(string Name, string State, string Summary);
