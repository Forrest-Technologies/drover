namespace Drover.Native.Pages;

/// <summary>The Limits section of the bot editor: what one piece of this bot's work may use before the server stops it.</summary>
public sealed class LimitsSectionView : EditorPart
{
    #region Private Fields

    private readonly Field steps;

    private readonly Field tokens;

    private readonly Field delegations;

    #endregion

    #region Constructors

    public LimitsSectionView(BotEditorViewModel editor)
        : base(editor)
    {
        steps = DraftField(LimitsSectionFields.StepsLabel, (draft, value) => draft.MaxSteps = value);
        tokens = DraftField(LimitsSectionFields.TokensLabel, (draft, value) => draft.MaxTokens = value);
        delegations = DraftField(LimitsSectionFields.DelegationsLabel, (draft, value) => draft.MaxDelegations = value);
        var pair = new PairPanel();
        pair.Children.Add(steps);
        pair.Children.Add(tokens);
        pair.Children.Add(delegations);
        Parts.Add(Styles.Text(LimitsSectionFields.Caption, "DvTextCaption"));
        Parts.Add(pair);
        Parts.Add(Styles.Text(LimitsSectionFields.FallbackCaption, "DvTextCaption"));
    }

    #endregion

    #region Private Methods

    protected override void Update()
    {
        var options = Editor.RunLimitOptions;
        (steps.Placeholder, steps.Hint) = (LimitsSectionFields.StepsPlaceholder(options?.MaxSteps), LimitsSectionFields.StepsHint(options));
        (tokens.Placeholder, tokens.Hint) = (LimitsSectionFields.Placeholder(options?.MaxTokens), LimitsSectionFields.TokensHint(options));
        (delegations.Placeholder, delegations.Hint) = (LimitsSectionFields.Placeholder(options?.MaxDelegations), LimitsSectionFields.DelegationsHint(options));
        steps.Show(Draft.MaxSteps);
        tokens.Show(Draft.MaxTokens);
        delegations.Show(Draft.MaxDelegations);
    }

    #endregion
}
