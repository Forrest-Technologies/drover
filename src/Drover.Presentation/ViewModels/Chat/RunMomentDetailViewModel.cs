namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// What one node of a run's timeline holds, opened under it: what a step wrote, a call's arguments, its output as it
/// streams, its result, a wait's answer, the task handed over and what came back. Everything a model or a tool wrote
/// is untrusted text: it is shown as text, never as markup, and machine text is set in the machine typeface. A
/// sandbox command reads as a terminal: the command, then what it printed.
/// </summary>
public sealed class RunMomentDetailViewModel : ObservableObject
{
    #region Private Fields

    // The log keeps this many characters of a call's result. A stored result says all of it.
    private const int ResultKept = 500;

    #endregion

    #region Properties

    public RunTreeView Tree { get; set; } = null!;

    public RunMoment Moment { get; set; } = null!;

    /// <summary>The name of the bot whose node this is.</summary>
    public string BotName { get; set; } = string.Empty;

    /// <summary>The name of the bot work went to, for a hand-off and its answer.</summary>
    public string ChildName { get; set; } = string.Empty;

    /// <summary>Sends a person's answer to a call that waits for one. Returns why it failed, or nothing when it was sent.</summary>
    public Func<Guid, bool, Task<string?>>? Decide { get; set; }

    /// <summary>Stored results of calls, where the conversation has them.</summary>
    public IReadOnlyDictionary<Guid, PartView>? Results { get; set; }

    /// <summary>True while an answer is on its way, and after one that went through, until the stream says the call moved on.</summary>
    public bool Deciding { get; private set; }

    /// <summary>Why the answer was not sent, as a sentence.</summary>
    public string? Problem { get; private set; }

    public RunMomentKind Kind => Moment.Kind;

    public ToolCard? Card => RunTimelines.CardOf(Tree, Moment, Results);

    /// <summary>The task a hand-off gave the other bot.</summary>
    public string? HandedTask => Card is { } card && JsonElement.Parse(card.Arguments) is { ValueKind: JsonValueKind.Object } arguments
        && arguments.TryGetProperty("task", out var task) && task.ValueKind is JsonValueKind.String
        ? task.GetString()
        : null;

    public IReadOnlyList<(string Name, string Value)> Facts =>
    [
        .. Took is { } took ? [("Took", Words.Duration(took))] : Array.Empty<(string, string)>(),
        .. Cost is { } cost ? [("Cost", cost)] : Array.Empty<(string, string)>(),
    ];

    /// <summary>What a step wrote, once it has written anything.</summary>
    public string? StepText => RunTimelines.TextOf(Tree, Moment) is { Length: > 0 } text ? text : null;

    /// <summary>True while a step is still being written.</summary>
    public bool StepStreaming => Moment.EndedAt is null;

    public string StepQuiet => Moment.EndedAt is null ? "Nothing written yet." : "It wrote nothing here and went on to its tools.";

    /// <summary>A sandbox command with something to show reads as a terminal.</summary>
    public bool IsCommand => Card is { } card && RunTimelines.CallOf(Tree, Moment)?.ToolName is "sandbox.run" && card.Summary.Length > 0;

    /// <summary>The command as typed, on the terminal's first line after the prompt.</summary>
    public string CommandLine => $"{Card?.Summary}\n";

    public bool ShowsArguments => !IsCommand && Card is { Arguments: not ("{}" or "") };

    public bool ShowsOutput => !IsCommand && Card is { Output.Count: > 0 };

    public bool ShowsResult => Card is { } card && (card.ExitCode is not null || card.Result is not null);

    public string? ExitLine => Card?.ExitCode is { } exitCode ? $"Exit code {exitCode}" : null;

    /// <summary>The log keeps only the start of a result. Said when that start is all there is.</summary>
    public bool ResultIsCut => Card is { } card && Results?.ContainsKey(card.Id) is not true && card.Result is { Length: >= ResultKept };

    public bool ShowsQuiet => Card is { IsLive: true, Output.Count: 0 };

    /// <summary>The titles of the parts a tool's details hold, in order.</summary>
    public IReadOnlyList<string> ToolPartTitles =>
    [
        .. IsCommand ? [CommandTitle] : Array.Empty<string>(),
        .. ShowsArguments ? [ArgumentsTitle] : Array.Empty<string>(),
        .. ShowsOutput ? [OutputTitle] : Array.Empty<string>(),
        .. ShowsResult ? [ResultTitle] : Array.Empty<string>(),
    ];

    /// <summary>A wait no one has answered yet, in a run still going: the person can answer it here, wherever in the tree it is.</summary>
    public bool IsOpenQuestion => Moment.Outcome is null && RunTimelines.CallOf(Tree, Moment) is { State: "awaiting_approval" };

    /// <summary>True when the wait is shown as a question with its two answers.</summary>
    public bool Asks => IsOpenQuestion && Decide is not null;

    public string Question => $"{BotName} asks to {Card?.Request ?? "go on"}.";

    public string Answered => (Moment.Outcome, Tree.RunOf(Moment.RunId).IsActive) switch
    {
        ("approved", _) => "This was approved.",
        ("denied", _) => "This was denied.",
        (_, false) => "The run ended before it was answered.",
        _ => "This waits for your answer.",
    };

    public bool ApprovalShowsArguments => Card is { Facts.Count: 0, Arguments: not ("{}" or "") };

    public string DelegationTitle => $"The task {BotName} handed to {ChildName}";

    public string AnswerTitle => Moment.Outcome is "completed" ? $"What {ChildName} answered" : $"Why {ChildName} has no answer";

    public string? AnswerText => Moment.Text is { Length: > 0 } answer ? answer : null;

    public string AnswerQuiet => Moment.Outcome is "cancelled" ? "The work was stopped before it was done." : "The work ended without an answer.";

    public string RuleSentence => Moment.Outcome switch
    {
        "redact" => $"The rule \"{Moment.Text}\" hid what matched.",
        "ask" => $"The rule \"{Moment.Text}\" asked you first.",
        "block" => $"The rule \"{Moment.Text}\" stopped the call.",
        "stop" => $"The rule \"{Moment.Text}\" stopped the run.",
        _ => $"The rule \"{Moment.Text}\" applied.",
    };

    public string? RuleWhy => Moment.Why is { Length: > 0 } why ? why : null;

    private TimeSpan? Took => Moment.Kind switch
    {
        RunMomentKind.Tool => RunTimelines.CallOf(Tree, Moment) is { IsOpen: false, DurationMs: > 0 } call ? TimeSpan.FromMilliseconds(call.DurationMs) : null,
        RunMomentKind.Answer or RunMomentKind.Rule => null,
        _ => Moment.EndedAt - Moment.At,
    };

    // A step's own model call, or for work handed over, everything the other bot's run used.
    private string? Cost => Moment switch
    {
        { Kind: RunMomentKind.Step, Cost: { } cost } => Money(cost, Moment.Currency),
        { Kind: RunMomentKind.Answer, ChildRunId: { } child } when Tree.RunOf(child).Usage.Cost > 0 => Money(Tree.RunOf(child).Usage.Cost, null),
        _ => null,
    };

    #endregion

    #region Public Methods

    // A successful answer keeps the buttons off until the stream says the call moved on, so a second press cannot
    // send a second decision.
    public void ParametersSet()
    {
        if (!IsOpenQuestion)
        {
            (Deciding, Problem) = (false, null);
        }
    }

    public async Task Answer(bool approved)
    {
        if (Moment.ToolCallId is not { } call || Decide is null)
        {
            return;
        }

        if (Deciding)
        {
            return;
        }

        (Deciding, Problem) = (true, null);
        Changed();
        Problem = await Decide(call, approved);
        Deciding = Problem is null;
        Changed();
    }

    #endregion

    #region Fixed Words

    public const string CommandTitle = "Command";

    public const string TerminalName = "Command and its output";

    public const string Prompt = "$ ";

    public const string ArgumentsTitle = "Arguments";

    public const string OutputTitle = "Output";

    public const string ResultTitle = "Result";

    public const string CutNote = "This is the start of the result, as the run's log keeps it.";

    public const string Quiet = "No output yet.";

    public const string Approve = "Approve";

    public const string Deny = "Deny";

    #endregion

    #region Private Methods

    private static string Money(decimal cost, string? currency) =>
        $"{cost.ToString("0.######", CultureInfo.InvariantCulture)}{(currency is { Length: > 0 } ? $" {currency}" : string.Empty)}";

    private void Changed() => OnPropertyChanged(string.Empty);

    #endregion
}
