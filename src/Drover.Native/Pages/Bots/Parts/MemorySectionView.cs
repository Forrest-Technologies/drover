namespace Drover.Native.Pages;

/// <summary>The Memory section of the bot editor.</summary>
public sealed class MemorySectionView : EditorPart
{
    #region Private Fields

    private readonly Switch remember;

    private readonly Select compaction;

    private readonly PairPanel numbers = new();

    private readonly Field compactAt;

    private readonly Field keepRecent;

    #endregion

    #region Constructors

    public MemorySectionView(BotEditorViewModel editor)
        : base(editor)
    {
        remember = new Switch { Label = MemorySectionFields.RememberLabel }.OnChanged(Quiet, on => Editor.Edit(draft => draft.MemoryEnabled = on));
        compaction = new Select { Label = MemorySectionFields.CompactionLabel }.OnChanged(Quiet, value => Editor.Edit(draft => draft.Compaction = value));
        compactAt = DraftField(MemorySectionFields.CompactAtLabel, (draft, value) => draft.CompactAtTokens = value, MemorySectionFields.CompactAtPlaceholder);
        keepRecent = DraftField(MemorySectionFields.KeepRecentLabel, (draft, value) => draft.KeepRecentMessages = value, MemorySectionFields.KeepRecentPlaceholder);
        numbers.Children.Add(compactAt);
        numbers.Children.Add(keepRecent);
        Parts.Add(new StatusNote { Text = MemorySectionFields.Note });
        Parts.Add(remember);
        Parts.Add(compaction);
        Parts.Add(numbers);
    }

    #endregion

    #region Private Methods

    protected override void Update()
    {
        remember.Show(Draft.MemoryEnabled);
        compaction.Show(MemorySectionFields.Strategies, Draft.Compaction);
        numbers.Shown(MemorySectionFields.NumbersShown(Draft));
        compactAt.Show(Draft.CompactAtTokens);
        keepRecent.Show(Draft.KeepRecentMessages);
    }

    #endregion
}
