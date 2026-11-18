namespace Drover.Native.Pages;

/// <summary>The Team section of the bot editor: how far this bot may go when it builds bots of its own.</summary>
public sealed class TeamSectionView : EditorPart
{
    #region Private Fields

    private readonly Field children;

    private readonly Field depth;

    #endregion

    #region Constructors

    public TeamSectionView(BotEditorViewModel editor)
        : base(editor)
    {
        children = DraftField(TeamSectionFields.ChildrenLabel, (draft, value) => draft.MaxChildren = value, TeamSectionFields.Placeholder);
        depth = DraftField(TeamSectionFields.DepthLabel, (draft, value) => draft.MaxDepthBelow = value, TeamSectionFields.Placeholder);
        var pair = new PairPanel();
        pair.Children.Add(children);
        pair.Children.Add(depth);
        Parts.Add(new StatusNote { Text = TeamSectionFields.Note });
        Parts.Add(pair);
    }

    #endregion

    #region Private Methods

    protected override void Update()
    {
        children.Show(Draft.MaxChildren);
        depth.Show(Draft.MaxDepthBelow);
    }

    #endregion
}
