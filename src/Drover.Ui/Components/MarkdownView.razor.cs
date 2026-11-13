namespace Drover.Ui.Components;

/// <summary>
/// Shows message text written in markdown. The text is untrusted: it is parsed into a fixed set of nodes by
/// <see cref="MarkdownDocument"/> and built element by element, so nothing in it is ever injected as markup.
/// Remote images wait behind a button until a person asks for them.
/// </summary>
public sealed partial class MarkdownView
{
    #region Private Fields

    private const string ImageWidth = "480";

    private const string ImageHeight = "270";

    private readonly HashSet<Uri> loadedImages = [];

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
            case ImageNode image when loadedImages.Contains(image.Source):
                RenderImage(builder, image);
                break;
            case ImageNode image:
                RenderImagePlaceholder(builder, image);
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

    private void RenderImagePlaceholder(RenderTreeBuilder builder, ImageNode image)
    {
        builder.OpenElement(40, "button");
        builder.AddAttribute(41, "type", "button");
        builder.AddAttribute(42, "class", "dv-markdown-image-load");
        builder.AddAttribute(43, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () => LoadImage(image)));
        builder.AddContent(44, PlaceholderLabel(image));
        builder.CloseElement();
    }

    private static void RenderImage(RenderTreeBuilder builder, ImageNode image)
    {
        builder.OpenElement(50, "img");
        builder.AddAttribute(51, "class", "dv-markdown-image");
        builder.AddAttribute(52, "src", image.Source.AbsoluteUri);
        builder.AddAttribute(53, "alt", image.Alt);
        builder.AddAttribute(54, "width", ImageWidth);
        builder.AddAttribute(55, "height", ImageHeight);
        builder.AddAttribute(56, "loading", "lazy");
        builder.AddAttribute(57, "referrerpolicy", "no-referrer");
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

    #region Remote Images

    private void LoadImage(ImageNode image) => loadedImages.Add(image.Source);

    private static string PlaceholderLabel(ImageNode image) =>
        string.IsNullOrWhiteSpace(image.Alt)
            ? $"Load image from {image.Source.Host}"
            : $"Load image from {image.Source.Host}: {image.Alt}";

    #endregion
}
