namespace Drover.Domain.Secrets;

/// <summary>How a sandbox secret reaches a bot's sandbox.</summary>
public enum SandboxSecretMode
{
    /// <summary>
    /// The sandbox holds a placeholder. The credential broker puts the real value into a request only when it goes to
    /// one of the secret's hosts, so the value never enters the sandbox.
    /// </summary>
    Brokered,

    /// <summary>
    /// The sandbox holds the real value. For credentials the broker cannot swap in transit, such as a key that signs
    /// requests inside the sandbox. A person must ask for it.
    /// </summary>
    Raw,
}
