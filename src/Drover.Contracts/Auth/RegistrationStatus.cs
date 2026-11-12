namespace Drover.Contracts.Auth;

/// <summary>
/// What registration asks of a new person: the version of the terms they must accept, or null when none is asked.
/// The endpoint answers 404 when this server does not take registrations.
/// </summary>
public sealed record RegistrationStatus(string? TermsVersion);
