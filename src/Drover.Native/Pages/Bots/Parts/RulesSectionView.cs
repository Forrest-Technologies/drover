namespace Drover.Native.Pages;

/// <summary>The Rules section of the bot editor: limits a person writes as "when, if, then" and the server enforces.</summary>
public sealed class RulesSectionView : EditorPart
{
    #region Private Fields

    private readonly TextBlock loading = BotParts.Hint(RulesSectionViewModel.LoadingRules);

    private readonly StackPanel loaded = new() { Spacing = Tokens.Number("DvSpace4") };

    private readonly TextBlock patternsOff = BotParts.Hint(RuleWords.WrittenPatternsOff);

    private readonly HiddenStatus announcement = new();

    private readonly NamedGroup inherited = new(RulesSectionViewModel.InheritedName, null);

    private readonly NamedGroup own = new(RulesSectionViewModel.OwnName, null);

    private readonly StackPanel starts = new() { Spacing = Tokens.Number("DvSpace4") };

    private readonly FlowPanel starters = new() { Gap = Tokens.Number("DvSpace2") };

    private readonly Button add;

    private readonly StatusNote startProblem = new() { Kind = NoteKind.Danger };

    private readonly ContentControl form = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };

    private readonly NamedGroup tryIt;

    private readonly Border tryFrame;

    private readonly TextBlock tryHint = BotParts.Hint();

    private readonly Select tryCheckpoint;

    private readonly Select tryTool;

    private readonly TextArea trySample;

    private readonly Button tryButton;

    private readonly StatusNote tryProblem = new() { Kind = NoteKind.Danger };

    private readonly TextBlock trial = Styles.Text(null);

    private readonly TextBlock leftLabel = BotParts.Hint(RulesSectionViewModel.WhatWouldBeLeft);

    private readonly Border left = new();

    private readonly TextBlock leftText = Styles.Text(null, "DvTextCode");

    private readonly Dictionary<Guid, RuleRowView> rows = [];

    private RuleDraft? shownDraft;

    private RuleForm? shownForm;

    #endregion

    #region Constructors

    public RulesSectionView(BotEditorViewModel editor)
        : base(editor)
    {
        var part = editor.RulesPart;
        AutomationProperties.SetLiveSetting(loading, AutomationLiveSetting.Polite);
        add = BotParts.IconAction(IconName.Plus, RulesSectionViewModel.WriteRule, part.Add);
        starts.Children.Add(add);
        starts.Children.Add(new NamedGroup(RulesSectionViewModel.StartersLegend, "DvSpace3", BotParts.ChoiceLabel(RulesSectionViewModel.StartersLegend), starters));
        starts.Children.Add(startProblem);

        tryCheckpoint = new Select { Label = RulesSectionViewModel.AsIfLabel }.OnChanged(Quiet, value => Change(() => part.TryCheckpoint = value));
        tryTool = new Select { Label = RulesSectionViewModel.ToolLabel }.OnChanged(Quiet, part.ChooseTryTool);
        trySample = new TextArea { Rows = 3 }.OnChanged(Quiet, value => part.TrySample = value);
        tryButton = BotParts.Action(RulesSectionViewModel.TryButton, () => _ = part.Try());
        var pair = new PairPanel();
        pair.Children.Add(tryCheckpoint);
        pair.Children.Add(tryTool);
        leftText.TextWrapping = TextWrapping.Wrap;
        leftText.IsTextSelectionEnabled = true;
        left.Child = leftText;
        left.Padding = new(Tokens.Number("DvSpace3"));
        left.WhenShownOrRethemed(() => left.Background = Tokens.Brush(left, "DvBg2"));
        var result = BotParts.Column("DvSpace2", trial, leftLabel, left);
        AutomationProperties.SetLiveSetting(result, AutomationLiveSetting.Polite);
        tryIt = new NamedGroup(
            RulesSectionViewModel.TryLegend, "DvSpace3", BotParts.ChoiceLabel(RulesSectionViewModel.TryLegend), tryHint, pair, trySample, tryButton, tryProblem, result);
        tryFrame = new Border { Child = tryIt, Padding = new(0, Tokens.Number("DvSpace4"), 0, 0), BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0) };
        tryFrame.WhenShownOrRethemed(() => tryFrame.BorderBrush = Tokens.Brush(tryFrame, "DvLine"));

        foreach (var child in new UIElement[] { BotParts.Hint(RuleWords.Intro), patternsOff, inherited, own, starts, form, tryFrame })
        {
            loaded.Children.Add(child);
        }

        Parts.Add(loading);
        Parts.Add(loaded);
        Beside(announcement);
    }

    #endregion

    #region Private Methods

    protected override void Update()
    {
        var part = Editor.RulesPart;
        loading.Shown(part.Loading);
        loaded.Shown(!part.Loading);

        if (part.Loading)
        {
            return;
        }

        patternsOff.Shown(!part.WrittenPatterns);
        announcement.Say(part.Announcement);
        ShowRules(inherited, part.Inherited, part.Busy);
        ShowRules(own, part.Own, part.Busy);
        starts.Shown(part.Draft is null);
        ShowStarters(part);
        startProblem.Show(part.Draft is null ? part.Problem : null);
        ShowForm(part);
        ShowTrial(part);
        MoveFocus(part);
    }

    // The row that was used is gone, so focus goes to the button that starts a new rule.
    private void MoveFocus(RulesSectionViewModel part)
    {
        if (part.TakeFocus() is RuleFocus.Add)
        {
            DispatcherQueue.TryEnqueue(() => add.TakeFocus());
        }
    }

    private void ShowRules(NamedGroup list, IReadOnlyList<BotRuleResponse> rules, bool busy)
    {
        List<UIElement> shown = [];

        foreach (var rule in rules)
        {
            if (!rows.TryGetValue(rule.Id, out var row))
            {
                row = new RuleRowView(Editor.RulesPart, rule);
                rows[rule.Id] = row;
            }

            row.Show(rule, busy);
            shown.Add(row);
        }

        list.Shown(shown.Count > 0);
        if (!list.Children.SequenceEqual(shown))
        {
            list.Fill(shown);
        }
    }

    private void ShowStarters(RulesSectionViewModel part)
    {
        if (starters.Children.Count == 0)
        {
            foreach (var starter in part.Starters)
            {
                starters.Children.Add(BotParts.Action(starter.Label, () => _ = part.AddStarter(starter)));
            }
        }

        foreach (var button in starters.Children.OfType<Button>())
        {
            button.IsEnabled = !part.Busy;
        }

        ((Button)starts.Children[0]).IsEnabled = !part.Busy;
    }

    // The form is built for each rule a person starts writing or opens, and kept while they work on it.
    private void ShowForm(RulesSectionViewModel part)
    {
        if (!ReferenceEquals(shownDraft, part.Draft))
        {
            shownDraft = part.Draft;
            shownForm = part.Draft is { } draft ? new RuleForm(part, draft) : null;
            form.Content = shownForm;
        }

        form.Shown(shownForm is not null);
        shownForm?.Show();
    }

    private void ShowTrial(RulesSectionViewModel part)
    {
        tryFrame.Shown(part.TryShown);
        tryHint.Show(part.TryHint);
        tryCheckpoint.Show([.. RuleWords.Checkpoints.Select(choice => new ChoiceOption(choice.Value, choice.Label))], part.TryCheckpoint);
        tryTool.Shown(part.TryToolShown);

        if (part.TryToolShown)
        {
            tryTool.Show([.. part.ToolNames.Select(tool => new ChoiceOption(tool, tool))], part.TryTool ?? string.Empty);
        }

        trySample.Label = part.TrySampleLabel;
        trySample.Machine = part.TryOnTool;
        trySample.Show(part.TrySample);
        tryButton.IsEnabled = !part.Trying;
        tryProblem.Show(part.TryProblem);
        trial.Shown(part.TrialWords is not null).Show(part.TrialWords);
        leftLabel.Shown(part.Left is not null);
        left.Shown(part.Left is not null);
        leftText.Show(part.Left);
    }

    private void Change(Action change)
    {
        change();
        Editor.Notify();
    }

    #endregion
}
