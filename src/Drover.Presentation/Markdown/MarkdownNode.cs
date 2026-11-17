namespace Drover.Ui.Markdown;

/// <summary>
/// One piece of a parsed message. The set of node kinds is the whole of what a message can put on screen:
/// anything the parser does not turn into one of these is not rendered.
/// </summary>
public abstract record MarkdownNode;

/// <summary>Plain text. Rendered as text, never as markup.</summary>
public sealed record TextNode(string Text) : MarkdownNode;

/// <summary>An element from the fixed list in <see cref="MarkdownDocument"/>. It carries no attributes from the source.</summary>
public sealed record ElementNode(string Name, IReadOnlyList<MarkdownNode> Children, int? Start = null) : MarkdownNode;

/// <summary>A link to an absolute http, https, or mailto address.</summary>
public sealed record LinkNode(string Address, IReadOnlyList<MarkdownNode> Children) : MarkdownNode;

/// <summary>A remote image at an absolute http or https address. It is not fetched until a person asks.</summary>
public sealed record ImageNode(Uri Source, string Alt) : MarkdownNode;

/// <summary>A fenced or indented code block.</summary>
public sealed record CodeNode(string Code, string? Language) : MarkdownNode;

/// <summary>The checkbox of a task list item.</summary>
public sealed record TaskNode(bool Done) : MarkdownNode;
