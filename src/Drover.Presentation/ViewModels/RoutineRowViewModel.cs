namespace Drover.Presentation.ViewModels;

/// <summary>
/// One routine: when it runs as a sentence, what it is sent, when it runs next, how the last run went, and its
/// controls. The sentence is the row's heading, so it is what is read out first.
/// </summary>
public sealed class RoutineRowViewModel
{
    #region Private Fields

    private int handledRun;

    #endregion

    #region Properties

    public required RoutineItem Routine { get; set; }

    public required string BotName { get; set; }

    /// <summary>A paused bot's routines do not run, and the row says so in place of the next time.</summary>
    public bool BotPaused { get; set; }

    /// <summary>Owners and admins change routines. Everyone else reads them.</summary>
    public bool CanChange { get; set; }

    /// <summary>
    /// Only the person who made a routine changes what it sends or when, because it runs as them. Another owner or
    /// admin may turn it off, run it once as themselves, or remove it.
    /// </summary>
    public bool CanRewrite { get; set; } = true;

    /// <summary>True while a change to this section is being asked for.</summary>
    public bool Busy { get; set; }

    /// <summary>The conversation a run started from this row went to, once it started.</summary>
    public string? StartedHref { get; set; }

    /// <summary>What the server said when it refused this row's last action, as a sentence.</summary>
    public string? Problem { get; set; }

    /// <summary>
    /// A new number for each run started from a dialog, which hands focus back to a button that was disabled while
    /// the run was asked for. Each new number moves focus once.
    /// </summary>
    public int FocusAfterRun { get; set; }

    /// <summary>Whether the whole prompt is shown, for someone who did not write it.</summary>
    public bool WholePrompt { get; private set; }

    public static string ReadWholePrompt => "Read the whole prompt";

    public static string NextTerm => "Next";

    public static string LastTerm => "Last";

    public static string OpenThatConversation => "Open that conversation";

    public static string StartedWords => "Started once now, as you.";

    public static string OpenStarted => "Open the conversation it started";

    public static string SwitchLabel => "Runs on its own";

    public static string RunOnce => "Run once now";

    public static string Change => "Change";

    public static string Remove => "Remove";

    public static string OnlyTheMaker => "Only the person who made it can change what it sends or when, because it runs as them.";

    public string Sentence => RoutineWords.Sentence(Routine.Cron, Routine.TimeZone);

    /// <summary>The expression itself, beside the sentence, when it is not one the editor can put in words.</summary>
    public string? Cron => RoutineWords.Describe(Routine.Cron) is null ? Routine.Cron : null;

    public string Prompt => WholePrompt ? Routine.Prompt : RoutineWords.FirstLine(Routine.Prompt);

    public bool ReadWholeShown => !CanRewrite && RoutineWords.FirstLine(Routine.Prompt) != Routine.Prompt.Trim();

    public string SwitchAbout => $"{Sentence}: {RoutineWords.FirstLine(Routine.Prompt)}";

    public string Next => (Routine.Enabled, BotPaused, Routine.NextRun) switch
    {
        (false, _, _) => "Off. It does not run until it is turned on.",
        (_, true, _) => $"Does not run while {BotName} is paused.",
        (_, _, { } next) => $"Runs {RoutineWords.Moment(next, Routine.TimeZone)}",
        _ => "Drover has not worked out the next time yet.",
    };

    public string Last =>
        RoutineWords.Outcome(Routine.LastRun, Routine.LastRun is { } last ? RoutineWords.Moment(last.When, Routine.TimeZone) : string.Empty);

    #endregion

    #region Public Methods

    public void ToggleWholePrompt() => WholePrompt = !WholePrompt;

    /// <summary>
    /// Where focus goes after a run asked for from the dialog, once for each new number: to the link to the
    /// conversation the run started, or back to "Run once now" when the server refused it, so the refusal is read
    /// beside it. Null when focus stays where it is.
    /// </summary>
    public RoutineFocus? TakeFocusAfterRun()
    {
        if (FocusAfterRun == 0 || FocusAfterRun == handledRun)
        {
            return null;
        }

        handledRun = FocusAfterRun;
        return Problem is null && StartedHref is not null ? RoutineFocus.Started : RoutineFocus.RunOnce;
    }

    #endregion
}
