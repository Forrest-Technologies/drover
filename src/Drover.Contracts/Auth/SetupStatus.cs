namespace Drover.Contracts.Auth;

/// <summary>Whether this caller must present the setup code. The endpoint answers 404 once setup is closed.</summary>
public sealed record SetupStatus(bool RequiresCode);
