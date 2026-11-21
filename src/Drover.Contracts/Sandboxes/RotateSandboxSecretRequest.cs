namespace Drover.Contracts.Sandboxes;

/// <summary>Input only: the new value of a sandbox secret. Never part of a response, and prints nothing when logged.</summary>
public sealed record RotateSandboxSecretRequest(string Value)
{
    public override string ToString() => nameof(RotateSandboxSecretRequest);
}
