namespace Drover.Ui.Tests.Rules;

/// <summary>
/// One attribute of a tag. <see cref="Value"/> is empty when the attribute has no value.
/// </summary>
public sealed record AttributeNode(string Name, string Value, int Line, int ValueOffset);
