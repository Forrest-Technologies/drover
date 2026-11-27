namespace Drover.Adapters.Conformance;

/// <summary>
/// The optional features an adapter says its test model has. <see cref="StructuredOutput"/>,
/// <see cref="ReasoningText"/> and <see cref="PdfInput"/> are checked both ways, against the model record; <see cref="ProtectedReasoning"/> and
/// <see cref="CachedUsage"/> are checked only where claimed.
/// </summary>
/// <param name="StructuredOutput">The model record says structured output, and a schema request returns it.</param>
/// <param name="ReasoningText">The model record says reasoning, and a reply carries reasoning text.</param>
/// <param name="ProtectedReasoning">A reply carries protected reasoning, and the next call of a tool loop sends it back unchanged.</param>
/// <param name="CachedUsage">Usage reports how many input tokens were read from the provider's cache.</param>
/// <param name="JsonObject">The provider answers in JSON with no schema. Where it does not, a bot set to JSON-only is
/// refused in words before anything is sent.</param>
/// <param name="PdfInput">The model record says the model reads PDFs, and a PDF goes on the wire as a document. Checked
/// both ways.</param>
public sealed record ConformanceClaims(
    bool StructuredOutput,
    bool ReasoningText,
    bool ProtectedReasoning,
    bool CachedUsage,
    bool JsonObject = true,
    bool PdfInput = false);
