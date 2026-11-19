namespace Drover.Ui.Components;

/// <summary>
/// Shows message text written in markdown. The text is untrusted: it is parsed into a fixed set of nodes by
/// <see cref="MarkdownDocument"/> and built element by element, so nothing in it is ever injected as markup.
/// Remote images are shown as links.
/// </summary>
public sealed partial class MarkdownView
{
    #region Private Fields

    private readonly RenderFragment content;

    private IReadOnlyList<MarkdownNode> nodes = [];

    private string? parsedText;

    #endregion

    #region Constructors

    public MarkdownView()
    {
        content = builder => RenderNodes(builder, nodes);
    }

    #endregion

    #region Properties

    [Parameter, EditorRequired]
    public string Text { get; set; } = string.Empty;

    /// <summary>True while more text is still arriving.</summary>
    [Parameter]
    public bool Streaming { get; set; }

    #endregion

    #region Parsing

    protected override void OnParametersSet()
    {
        if (string.Equals(Text, parsedText, StringComparison.Ordinal))
        {
            return;
        }

        nodes = MarkdownDocument.Parse(Text);
        parsedText = Text;
    }

    #endregion

    #region Rendering

    private void RenderNodes(RenderTreeBuilder builder, IReadOnlyList<MarkdownNode> children)
    {
        foreach (var node in children)
        {
            RenderNode(builder, node);
        }
    }

    private void RenderNode(RenderTreeBuilder builder, MarkdownNode node)
    {
        switch (node)
        {
            case TextNode text:
                builder.AddContent(0, text.Text);
                break;
            case ElementNode { Name: "table" } table:
                RenderTable(builder, table);
                break;
            case ElementNode element:
                RenderElement(builder, element);
                break;
            case LinkNode link:
                RenderLink(builder, link);
                break;
            case ImageNode image:
                RenderLink(builder, image.ToExternalLink());
                break;
            case CodeNode code:
                RenderCode(builder, code);
                break;
            case TaskNode task:
                RenderTask(builder, task);
                break;
        }
    }

    private void RenderElement(RenderTreeBuilder builder, ElementNode element)
    {
        builder.OpenElement(10, element.Name);

        if (element.Start is { } start)
        {
            builder.AddAttribute(11, "start", start.ToString(CultureInfo.InvariantCulture));
        }

        RenderNodes(builder, element.Children);
        builder.CloseElement();
    }

    /// <summary>A wide table scrolls inside its own box, which takes focus so the keyboard can scroll it.</summary>
    private void RenderTable(RenderTreeBuilder builder, ElementNode table)
    {
        builder.OpenElement(20, "div");
        builder.AddAttribute(21, "class", "dv-markdown-table");
        builder.AddAttribute(22, "tabindex", "0");
        builder.AddAttribute(23, "role", "group");
        builder.AddAttribute(24, "aria-label", "Table");
        RenderElement(builder, table);
        builder.CloseElement();
    }

    private void RenderLink(RenderTreeBuilder builder, LinkNode link)
    {
        builder.OpenElement(30, "a");
        builder.AddAttribute(31, "href", link.Address);
        builder.AddAttribute(32, "rel", "noopener noreferrer");
        builder.AddAttribute(33, "target", "_blank");
        RenderNodes(builder, link.Children);
        builder.CloseElement();
    }

    private static void RenderCode(RenderTreeBuilder builder, CodeNode code)
    {
        builder.OpenComponent<CodeBlock>(60);
        builder.AddComponentParameter(61, nameof(CodeBlock.Code), code.Code);
        builder.AddComponentParameter(62, nameof(CodeBlock.Language), code.Language);
        builder.CloseComponent();
    }

    private static void RenderTask(RenderTreeBuilder builder, TaskNode task)
    {
        builder.OpenElement(70, "input");
        builder.AddAttribute(71, "type", "checkbox");
        builder.AddAttribute(72, "disabled", true);
        builder.AddAttribute(73, "checked", task.Done);
        builder.AddAttribute(74, "aria-label", task.Done ? "Done" : "Not done");
        builder.CloseElement();
    }

    #endregion
}
