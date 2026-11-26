namespace Drover.Adapters.Conformance;

/// <summary>The optional features an adapter says its test model has. The suite checks the claim both ways.</summary>
/// <param name="StructuredOutput">The model record says structured output, and a schema request returns it.</param>
/// <param name="ReasoningText">The model record says reasoning, and a reply carries reasoning text.</param>
/// <param name="ProtectedReasoning">A reply carries protected reasoning, and the next call of a tool loop sends it back unchanged.</param>
/// <param name="CachedUsage">Usage reports how many input tokens were read from the provider's cache.</param>
public sealed record ConformanceClaims(bool StructuredOutput, bool ReasoningText, bool ProtectedReasoning, bool CachedUsage);
