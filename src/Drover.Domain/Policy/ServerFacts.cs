namespace Drover.Domain.Policy;

/// <summary>What the server found about itself at start, which some rules are checked against.</summary>
public sealed record ServerFacts(bool KeyRingProtectedByCertificate, bool HoldsMigrationCredential);
