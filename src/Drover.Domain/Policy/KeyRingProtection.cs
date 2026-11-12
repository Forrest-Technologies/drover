namespace Drover.Domain.Policy;

/// <summary>How the key ring that protects stored secrets is itself protected. Ordered from strictest to loosest.</summary>
public enum KeyRingProtection
{
    CertificateOrKeyService,
    FilePermissions,
}
