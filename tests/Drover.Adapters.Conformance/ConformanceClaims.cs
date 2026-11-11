namespace Drover.Adapters.Conformance;

/// <summary>The optional features an adapter says its test model has. The suite checks the claim both ways.</summary>
public sealed record ConformanceClaims(bool StructuredOutput, bool ReasoningText);
