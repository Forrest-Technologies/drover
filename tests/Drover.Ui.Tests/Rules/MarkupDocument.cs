namespace Drover.Ui.Tests.Rules;

/// <summary>
/// A Razor file read for rule checks: its tags in order, its text content with everything else masked,
/// and the styles it carries in <c>style</c> elements and attributes.
/// </summary>
public sealed record MarkupDocument(IReadOnlyList<MarkupTag> Tags, string Text, IReadOnlyList<StyleRule> Styles);
