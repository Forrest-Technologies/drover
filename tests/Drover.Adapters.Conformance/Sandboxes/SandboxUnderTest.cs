namespace Drover.Adapters.Conformance.Sandboxes;

/// <summary>
/// A sandbox provider wired for one scenario: canned answers offline, the real service live. <see cref="Spec"/> is what
/// the scenario creates, <see cref="WorkFolder"/> is a folder the sandbox's user may write in, and
/// <see cref="SentRequests"/> returns what the adapter has sent so far.
/// </summary>
public sealed record SandboxUnderTest(
    ISandboxProvider Provider,
    SandboxSpec Spec,
    string WorkFolder,
    Func<IReadOnlyList<WireRequest>> SentRequests);
