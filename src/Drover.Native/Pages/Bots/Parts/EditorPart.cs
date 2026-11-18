namespace Drover.Native.Pages;

/// <summary>
/// One section of the bot editor, in the body of its frame: its parts stacked with the gap a section's body has.
/// It is built once and shown again whenever the editor's view model says something changed, writing only what
/// differs, so a field being typed in keeps its text and its focus.
/// </summary>
public abstract class EditorPart : Grid
{
    #region Private Fields

    private readonly StackPanel parts = new() { Spacing = Tokens.Number("DvSpace4") };

    #endregion

    #region Constructors

    protected EditorPart(BotEditorViewModel editor)
    {
        Editor = editor;
        Children.Add(parts);
    }

    #endregion

    #region Properties

    /// <summary>The section's parts, top to bottom.</summary>
    protected UIElementCollection Parts => parts.Children;

    protected BotEditorViewModel Editor { get; }

    protected BotDraft Draft => Editor.Draft!;

    /// <summary>True while the section writes the view model's state into its controls, which is not a person's change.</summary>
    protected bool Showing { get; private set; }

    #endregion

    #region Public Methods

    public void Show()
    {
        if (Editor.Draft is null)
        {
            return;
        }

        Showing = true;
        try
        {
            Update();
        }
        finally
        {
            Showing = false;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>Writes what the view model holds now into the section's controls.</summary>
    protected abstract void Update();

    protected bool Quiet() => Showing;

    /// <summary>
    /// Puts something in the section that takes no place among its parts: words only read out, or a dialog, as the
    /// web's visually hidden elements stand outside the column's gaps.
    /// </summary>
    protected void Beside(UIElement element) => Children.Add(element);

    /// <summary>A field whose text goes to the draft as it is typed.</summary>
    protected Field DraftField(string label, Action<BotDraft, string> write, string? placeholder = null, string? hint = null) =>
        new Field { Label = label, Placeholder = placeholder, Hint = hint }.OnChanged(Quiet, value => Editor.Edit(draft => write(draft, value)));

    #endregion
}
