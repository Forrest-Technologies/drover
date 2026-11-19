namespace Drover.Native.Chat;

/// <summary>
/// What a bot reasoned before its words, closed until a person opens it. While the reasoning arrives, the frame's edge
/// takes the live color and what arrives fades in as a reply's words do; once the words begin, it lies still. With
/// reduced motion the text simply appears.
/// </summary>
public sealed class ReplyReasoning : Grid
{
    #region Private Fields

    private readonly Disclosure section = new() { Title = "Reasoning", TitleWeight = "DvWeightLabel" };

    private readonly MarkdownView text = new();

    #endregion

    #region Constructors

    public ReplyReasoning()
    {
        section.Body = text;
        Children.Add(section);
    }

    #endregion

    #region Public Methods

    /// <summary>Shows the reasoning so far. <paramref name="arriving"/> is true while more of it is on its way.</summary>
    public void Show(string reasoning, bool arriving)
    {
        text.Streaming = arriving;
        text.Text = reasoning;
        section.Edge = arriving ? "DvIon" : null;
    }

    #endregion
}
