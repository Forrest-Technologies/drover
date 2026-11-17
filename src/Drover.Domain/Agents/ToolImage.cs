namespace Drover.Domain.Agents;

/// <summary>A picture a tool produced, such as a screenshot, for the model to look at once.</summary>
public sealed record ToolImage(ReadOnlyMemory<byte> Data, string MediaType);
