namespace Drover.Presentation.ViewModels;

/// <summary>The Rules section of the bot editor: limits a person writes as "when, if, then" and the server enforces.</summary>
public sealed class RulesSectionViewModel(BotEditorViewModel editor, DroverApi api, ILogger logger)
{
    #region Private Fields

    private IReadOnlyList<RuleStarter> starters = RuleStarters.For(TimeZone);

    private string tryTool = string.Empty;

    private RuleFocus? focus;

    #endregion

    #region Properties

    public static string LoadingRules => "Loading the rules.";

    public static string InheritedName => "Rules from the bots above";

    public static string OwnName => "Rules";

    public static string SwitchLabel => "On";

    public static string WriteRule => "Write a rule";

    public static string StartersLegend => "Starters. Each adds a rule you can then change.";

    public static string WhenLabel => "When";

    public static string NoTools => "This bot has no tools yet. Give it one in the Tools section first.";

    public static string ToolsLegend => "Tools";

    public static string ToolKindLegend => "The tool";

    public static string EntriesLabel => "Any of these, one on each line";

    public static string WhatLabel => "What";

    public static string PatternLabel => "Pattern";

    public static string PatternHint => "A regular expression. Lookarounds and backreferences are not accepted.";

    public static string InputNamesLabel => "Input names";

    public static string InputNamesHint => "Parted by commas, such as to, cc, bcc.";

    public static string AllowedLabel => "Allowed, one on each line";

    public static string AllowedHint => "Each value has to be one whole entry, as written. A domain such as example.org allows its addresses and subdomains.";

    public static string AddressesHint => "A domain such as example.org allows its addresses and subdomains.";

    public static string FromLabel => "From";

    public static string ToLabel => "To";

    public static string TimeZoneLabel => "Time zone";

    public static string TimeZoneHint => "An IANA name, such as Europe/Berlin.";

    public static string DaysLegend => "Only on these days. None chosen means every day.";

    public static string MoreThanLabel => "More than";

    public static string OfLabel => "Of";

    public static string ThenLabel => "Then";

    public static string NameLabel => "Name";

    public static string NameHint => "Shown in the run when the rule applies.";

    public static string ReadsAs => "Reads as";

    public static string Cancel => "Cancel";

    public static string TryLegend => "Try it";

    public static string AsIfLabel => "As if";

    public static string ToolLabel => "Tool";

    public static string TryButton => "Try the rules";

    public static string WhatWouldBeLeft => "What would be left";

    public IReadOnlyList<RuleStarter> Starters => starters;

    public bool Loading => editor.Rules is null;

    public bool WrittenPatterns => editor.WrittenPatterns;

    public IReadOnlyList<BotRuleResponse> Inherited => [.. editor.Rules!.Where(rule => rule.Inherited)];

    public IReadOnlyList<BotRuleResponse> Own => [.. editor.Rules!.Where(rule => !rule.Inherited)];

    /// <summary>The rule being written or changed, or null while none is.</summary>
    public RuleDraft? Draft { get; private set; }

    public Guid? EditedId { get; private set; }

    public bool Busy { get; private set; }

    public string? Problem { get; private set; }

    public string? Announcement { get; private set; }

    public string TryCheckpoint { get; set; } = RuleWords.BeforeTool;

    public string TrySample { get; set; } = string.Empty;

    public bool Trying { get; private set; }

    public string? TryProblem { get; private set; }

    public TryRulesResponse? Trial { get; private set; }

    public string FormLegend => EditedId is null ? "New rule" : "Change rule";

    public string SaveLabel => EditedId is null ? "Add rule" : "Save rule";

    public string AddConditionLabel => Draft!.Conditions.Count == 0 ? "Add a condition" : "Add another condition";

    public IReadOnlyList<RuleChoice> Effects => RuleWords.Effects(Draft!.Checkpoint);

    public string Preview => RuleWords.Sentence(Draft!.Checkpoint, Draft.Effect, Draft.ToBody().Conditions!);

    /// <summary>The trial is offered once there is a rule to try, or one being written.</summary>
    public bool TryShown => editor.Rules!.Count > 0 || Draft is not null;

    public string TryHint =>
        $"The server checks a sample against these rules, the way a run would. Nothing is run or saved.{(Draft is not null && EditedId is null ? " The rule being written is tried too." : null)}";

    public IReadOnlyList<string> ToolNames =>
        [.. (editor.Tools ?? []).Where(tool => tool.Grant != "off").Select(tool => tool.Name).Order(StringComparer.Ordinal)];

    public bool TryOnTool => TryCheckpoint is RuleWords.BeforeTool or RuleWords.AfterTool;

    public bool TryToolShown => TryOnTool && ToolNames.Count > 0;

    public string? TryTool => ToolNames.Contains(tryTool) ? tryTool : ToolNames is [var first, ..] ? first : null;

    public string TrySampleLabel => TryOnTool ? "Sample, as JSON" : "Sample text";

    public string? TrialWords => Trial is null ? null : RuleWords.Trial(Trial);

    /// <summary>What a trial leaves of the sample, as the machine text it is, or null when it leaves it whole.</summary>
    public string? Left => Trial?.RedactedJson ?? Trial?.RedactedText;

    private static string TimeZone => TimeZoneInfo.Local.HasIanaId ? TimeZoneInfo.Local.Id : "UTC";

    #endregion

    #region Public Methods

    public static string InheritedLine(BotRuleResponse rule) => $"{rule.Name} · from {rule.BotName}, changed there{(rule.Enabled ? null : " · off")}";

    public static string ChangeLabel(BotRuleResponse rule) => $"Change the rule {rule.Name}";

    public static string RemoveLabel(BotRuleResponse rule) => $"Remove the rule {rule.Name}";

    public static string RemovedAnnouncement(BotRuleResponse rule) => $"Rule removed: {rule.Name}.";

    public static string PutBackAnnouncement(BotRuleResponse rule) => $"Rule put back: {rule.Name}.";

    public static string UndoName(BotRuleResponse rule) => $"Undo removing {rule.Name}";

    public static string PutBackFailed(string sentence) => $"The rule could not be put back. {sentence}";

    public static string ConditionName(int number) => $"Condition {number}";

    public static string IfLabel(int number) => number == 1 ? "If" : "And if";

    public static string RemoveConditionLabel(int number) => $"Remove condition {number}";

    /// <summary>The kinds a condition may be. A rule that already has a written pattern keeps showing it, so opening the rule does not change it unseen.</summary>
    public IReadOnlyList<RuleChoice> ConditionChoices(ConditionDraft condition) =>
        RuleWords.Conditions(Draft!.Checkpoint, WrittenPatterns || condition.Kind is "text_matches");

    public void Add()
    {
        Draft = new RuleDraft();
        Draft.Conditions.Add(new ConditionDraft { Kind = "tool_kind", TimeZone = TimeZone });
        EditedId = null;
        Problem = null;
        editor.Notify();
    }

    public void Edit(BotRuleResponse rule)
    {
        Draft = RuleDraft.Of(rule, TimeZone);
        EditedId = rule.Id;
        Problem = null;
        editor.Notify();
    }

    public void CancelDraft()
    {
        Draft = null;
        editor.Notify();
    }

    public void MoveTo(string checkpoint)
    {
        Draft!.MoveTo(checkpoint);
        editor.Notify();
    }

    public void AddCondition()
    {
        Draft!.Conditions.Add(new ConditionDraft { TimeZone = TimeZone });
        editor.Notify();
    }

    public void RemoveCondition(ConditionDraft condition)
    {
        Draft!.Conditions.Remove(condition);
        editor.Notify();
    }

    /// <summary>Changes a part of the rule being written, as a field does.</summary>
    public void Change(Action change)
    {
        change();
        editor.Notify();
    }

    public void Check(HashSet<string> chosen, string value, bool on)
    {
        if (on)
        {
            chosen.Add(value);
        }
        else
        {
            chosen.Remove(value);
        }

        editor.Notify();
    }

    public void ChooseTryTool(string tool)
    {
        tryTool = tool;
        editor.Notify();
    }

    /// <summary>Where the focus should move now, once: the add button when a row went away.</summary>
    public RuleFocus? TakeFocus()
    {
        var wanted = focus is RuleFocus.Add && Draft is null ? focus : null;
        if (wanted is not null)
        {
            focus = null;
        }

        return wanted;
    }

    /// <summary>The undo came back while this section is on screen: put the rule in its place and say so here.</summary>
    public void PutBack(BotRuleResponse saved)
    {
        Announcement = PutBackAnnouncement(saved);
        editor.Rules = With(editor.Rules ?? [], saved);
        editor.Notify();
    }

    public Task Save()
    {
        var body = Draft!.ToBody();
        return Send(async () =>
        {
            var saved = EditedId is { } id ? await api.UpdateBotRule(editor.SpaceId, editor.BotId, id, body) : await api.CreateBotRule(editor.SpaceId, editor.BotId, body);
            Announcement = Said($"Rule saved. {RuleWords.Sentence(saved)}", saved);
            Draft = null;
            return With(editor.Rules!, saved);
        });
    }

    public Task AddStarter(RuleStarter starter) =>
        Send(async () =>
        {
            var rules = editor.Rules!;
            List<string> sentences = [];

            foreach (var body in starter.Rules)
            {
                var saved = await api.CreateBotRule(editor.SpaceId, editor.BotId, body);
                rules = With(rules, saved);
                sentences.Add(RuleWords.Sentence(saved));
            }

            Announcement = $"{(sentences.Count == 1 ? "Rule added." : $"{sentences.Count} rules added.")} {string.Join(' ', sentences)}";
            return rules;
        });

    public Task SwitchRule(BotRuleResponse rule, bool on) =>
        Send(async () =>
        {
            var saved = await api.UpdateBotRule(editor.SpaceId, editor.BotId, rule.Id, new BotRuleBody(rule.Name, on, rule.Checkpoint, rule.Effect, rule.Conditions));
            Announcement = Said($"Rule {(on ? "on" : "off")}. {RuleWords.Sentence(saved)}", saved);
            return With(editor.Rules!, saved);
        });

    public Task Remove(BotRuleResponse rule) =>
        Send(async () =>
        {
            await api.RemoveBotRule(editor.SpaceId, editor.BotId, rule.Id);
            Announcement = RemovedAnnouncement(rule);
            editor.OfferRuleUndo(rule);
            focus = RuleFocus.Add;

            if (EditedId == rule.Id)
            {
                Draft = null;
            }

            return [.. editor.Rules!.Where(other => other.Id != rule.Id)];
        });

    public async Task Try()
    {
        Trying = true;
        TryProblem = null;
        Trial = null;
        editor.Notify();

        try
        {
            var tool = TryOnTool ? TryTool : null;
            var sample = new TryRulesRequest(
                TryCheckpoint,
                tool,
                TryOnTool ? TrySample : null,
                TryOnTool ? null : TrySample,
                Draft is not null && EditedId is null ? Draft.ToBody() : null);
            Trial = await api.TryBotRules(editor.SpaceId, editor.BotId, sample);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(RulesSectionViewModel));
            TryProblem = Failures.Sentence(exception, "Check that the server is running.");
        }
        finally
        {
            Trying = false;
            editor.Notify();
        }
    }

    /// <summary>The section was closed: the rule being written and what the section said go with it, as on the web.</summary>
    public void Reset()
    {
        starters = RuleStarters.For(TimeZone);
        (Draft, EditedId, Busy, Problem, Announcement, focus) = (null, null, false, null, null, null);
        (TryCheckpoint, tryTool, TrySample, Trying, TryProblem, Trial) = (RuleWords.BeforeTool, string.Empty, string.Empty, false, null, null);
    }

    #endregion

    #region Private Methods

    // What is read out about a saved rule: what was done, and whatever the server notes about the rule.
    private static string Said(string done, BotRuleResponse saved) => string.Join(' ', [done, .. saved.Notes]);

    // The bots above come first, as a run reads them; a bot's own rules keep their order.
    private static IReadOnlyList<BotRuleResponse> With(IReadOnlyList<BotRuleResponse> rules, BotRuleResponse saved) =>
        [.. rules.Where(rule => rule.Id != saved.Id).Append(saved).OrderByDescending(rule => rule.Inherited).ThenBy(rule => rule.Inherited ? 0 : rule.Position)];

    private async Task Send(Func<Task<IReadOnlyList<BotRuleResponse>>> change)
    {
        Busy = true;
        Problem = null;
        editor.Notify();

        try
        {
            editor.Rules = await change();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(RulesSectionViewModel));
            Problem = Failures.Sentence(exception, "Check that the server is running.");
        }
        finally
        {
            Busy = false;
            editor.Notify();
        }
    }

    #endregion
}
