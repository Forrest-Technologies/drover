namespace Drover.Native.Pages;

/// <summary>The Limits section of the bot editor: what one piece of this bot's work may use before the server stops it.</summary>
public sealed class LimitsSectionView : EditorPart
{
    #region Private Fields

    private readonly Field steps;

    private readonly Field tokens;

    private readonly Field delegations;

    private readonly Switch unlimited;

    private readonly CheckItem acknowledge = new("I understand that removing the token limit can cause API overspending if my API key has no spending limit.");

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
        unlimited = new Switch { Label = "Unlimited tokens" }.OnChanged(Quiet, on => Editor.Edit(draft =>
        {
            draft.UnlimitedTokens = on;
            draft.UncappedUsageAcknowledged = false;
        }));
        acknowledge.Changed += on => Editor.Edit(draft => draft.UncappedUsageAcknowledged = on);
        var tokenSettings = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        tokenSettings.Children.Add(tokens);
        tokenSettings.Children.Add(unlimited);
        tokenSettings.Children.Add(acknowledge);
        pair.Children.Add(tokenSettings);
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
        (steps.Placeholder, steps.Hint) = (LimitsSectionFields.StepsPlaceholder(options), LimitsSectionFields.StepsHint(options));
        (tokens.Placeholder, tokens.Hint) = (LimitsSectionFields.Placeholder(options?.MaxTokens), LimitsSectionFields.TokensHint(options));
        (delegations.Placeholder, delegations.Hint) = (LimitsSectionFields.Placeholder(options?.MaxDelegations), LimitsSectionFields.DelegationsHint(options));
        steps.Show(Draft.MaxSteps);
        unlimited.Shown(options?.CanRemoveUsageLimit is true || Draft.UnlimitedTokens);
        unlimited.Show(Draft.UnlimitedTokens);
        acknowledge.Shown(Draft.UnlimitedTokens);
        acknowledge.Show(Draft.UncappedUsageAcknowledged);
        tokens.Shown(!Draft.UnlimitedTokens);
        tokens.Show(Draft.UnlimitedTokens ? string.Empty : Draft.MaxTokens);
        delegations.Show(Draft.MaxDelegations);
    }

    #endregion
}
