namespace Drover.Domain.Tools;

/// <summary>A server that returns open-world content is untrusted; a run that read from one is recorded as tainted. The record changes no grant.</summary>
public enum TrustLevel
{
    Trusted,
    Untrusted,
}
