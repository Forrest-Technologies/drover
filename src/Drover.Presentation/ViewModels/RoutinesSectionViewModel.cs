namespace Drover.Presentation.ViewModels;

/// <summary>
/// The Routines section of the bot editor. Its job: decide what this bot does on its own and when, and check that
/// it did.
/// </summary>
public sealed class RoutinesSectionViewModel(BotEditorViewModel editor, DroverApi api, SessionState session, ILogger logger)
{
    #region Private Fields

    private static readonly TimeSpan checkPause = TimeSpan.FromMilliseconds(300);

    private static readonly SegmentOption<string>[] threadModes = [new("new_thread", "A new conversation"), new("same_thread", "The same conversation")];

    private readonly Dictionary<Guid, string> rowProblems = [];

    private readonly Dictionary<Guid, string> started = [];

    private readonly Dictionary<Guid, RoutineRowViewModel> rows = [];

    private bool editedEnabled;

    private int checks;

    private (Guid? Id, int Ticket) focusRun;

    private RoutineFocus? focus;

    #endregion

    #region Properties

    public static string LoadingRoutines => "Loading the routines.";

    public static string ListName => "Routines";

    public static string WhereLabel => "Where it answers";

    public static IReadOnlyList<SegmentOption<string>> ThreadModes => threadModes;

    public static string SavedOff => "It is saved turned off. Run it once to try it, then turn it on.";

    public static string Cancel => "Cancel";

    public static string AddRoutine => "Add a routine";

    public static string RunTitle => "Run someone else's routine once, as you";

    public static string DoNotRun => "Do not run it";

    public static string RunConfirm => "Run it once now";

    public static string RemoveTitle => "Remove this routine";

    public static string RemoveWords => "It stops running. The conversations it started are kept.";

    public static string KeepRoutine => "Keep routine";

    public static string RemoveRoutine => "Remove routine";

    public bool Loading => editor.Schedules is null;

    public IReadOnlyList<BotScheduleResponse> Schedules => editor.Schedules ?? [];

    /// <summary>Owners and admins change routines. Everyone else reads them.</summary>
    public bool CanChange => session.Space?.Role is "owner" or "admin";

    public bool Editing { get; private set; }

    public Guid? EditedId { get; private set; }

    public string Prompt { get; set; } = string.Empty;

    public RoutineWhen When { get; private set; } = RoutineWhen.Start;

    public string TimeZone { get; private set; } = string.Empty;

    public string ThreadMode { get; set; } = "new_thread";

    public bool Busy { get; private set; }

    public string? Refusal { get; private set; }

    public IReadOnlyList<DateTimeOffset>? Next { get; private set; }

    public string? FormProblem { get; private set; }

    public string? Announcement { get; private set; }

    public bool ConfirmingRun { get; private set; }

    public bool Confirming { get; private set; }

    /// <summary>The routine the person is asked to read before running it as their own.</summary>
    public BotScheduleResponse? Running { get; private set; }

    /// <summary>The routine the person is asked about removing.</summary>
    public BotScheduleResponse? Removing { get; private set; }

    /// <summary>Said when there is no routine and none is being written, or null when something else is shown.</summary>
    public string? EmptyWords => (Schedules.Count, Editing, CanChange) switch
    {
        ( > 0, _, _) or (_, true, _) => null,
        (_, _, true) => $"A routine is a prompt {BotName} is sent on its own, at the times you choose.",
        _ => $"{BotName} has no routines. Owners and admins of this space add them.",
    };

    public bool AddShown => !Editing && CanChange;

    public string FormLegend => EditedId is null ? "New routine" : "Change routine";

    public string PromptLabel => $"What to ask {BotName}";

    public bool SavedOffShown => EditedId is null;

    public string SaveLabel => EditedId is null ? "Add routine" : "Save routine";

    public string Reads => RoutineWhenFields.Reads(When, TimeZone, Refusal, Next);

    public string RunWords => Running is { } shown
        ? $"{RoutineWords.Sentence(shown.Cron, shown.TimeZone)}. {BotName} is sent this, in a conversation of yours:"
        : string.Empty;

    public string RemovingWords => Removing is { } shown ? $"{RoutineWords.Sentence(shown.Cron, shown.TimeZone)}: {RoutineWords.FirstLine(shown.Prompt)}" : string.Empty;

    private string BotName => editor.Saved!.Name;

    private static string OwnTimeZone => TimeZoneInfo.Local.HasIanaId ? TimeZoneInfo.Local.Id : "UTC";

    #endregion

    #region Public Methods

    /// <summary>The row of one routine, kept from one change to the next, so what a person opened in it stays open.</summary>
    public RoutineRowViewModel Row(BotScheduleResponse schedule)
    {
        if (!rows.TryGetValue(schedule.Id, out var row))
        {
            row = new() { Routine = RoutineWords.Item(schedule, BotName), BotName = BotName };
            rows[schedule.Id] = row;
        }

        row.Routine = RoutineWords.Item(schedule, BotName);
        row.BotName = BotName;
        row.BotPaused = editor.IsPaused;
        row.CanChange = CanChange;
        row.CanRewrite = schedule.OwnerUserId == session.Current?.UserId;
        row.Busy = Busy;
        row.Problem = rowProblems.GetValueOrDefault(schedule.Id);
        row.StartedHref = started.GetValueOrDefault(schedule.Id);
        row.FocusAfterRun = schedule.Id == focusRun.Id ? focusRun.Ticket : 0;
        return row;
    }

    /// <summary>Where the focus should move now, once: the form that opened, or the add area that came back.</summary>
    public RoutineFocus? TakeFocus()
    {
        var wanted = (focus, Editing) switch
        {
            (RoutineFocus.Form, true) or (RoutineFocus.Add, false) => focus,
            _ => null,
        };

        if (wanted is not null)
        {
            focus = null;
        }

        return wanted;
    }

    public void Add() => Open(null, false, string.Empty, RoutineWhen.Start, OwnTimeZone, "new_thread");

    public void Edit(BotScheduleResponse schedule) =>
        Open(schedule.Id, schedule.Enabled, schedule.Prompt, RoutineWords.Read(schedule.Cron), schedule.TimeZone, schedule.ThreadMode);

    public void CancelForm()
    {
        (Editing, focus) = (false, RoutineFocus.Add);
        editor.Notify();
    }

    public Task ChangeWhen(RoutineWhen chosen)
    {
        When = chosen;
        return CheckTime();
    }

    public Task ChangeTimeZone(string zone)
    {
        TimeZone = zone;
        return CheckTime();
    }

    /// <summary>
    /// Asks the server what saving the time would come to, once typing pauses, so an expression is explained or
    /// refused before it is saved. An answer for a time that has changed since is dropped.
    /// </summary>
    public async Task CheckTime()
    {
        (Refusal, Next) = (null, null);
        editor.Notify();
        var asked = ++checks;
        await Task.Delay(checkPause);

        if (asked != checks || RoutineWords.Cron(When) is not { } cron)
        {
            return;
        }

        try
        {
            var check = await api.CheckBotSchedule(editor.SpaceId, editor.BotId, new ScheduleCheckBody(cron, TimeZone.Trim()));

            if (asked == checks)
            {
                (Refusal, Next) = (check.Problem, check.Next);
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(RoutinesSectionViewModel));
        }

        editor.Notify();
    }

    // A new routine is saved off: the person tries it once, then turns it on.
    public async Task Save()
    {
        FormProblem = (string.IsNullOrWhiteSpace(Prompt), RoutineWords.Cron(When)) switch
        {
            (true, _) => $"Write what to ask {BotName}.",
            (_, null) => "Finish choosing when it runs.",
            _ => null,
        };

        if (FormProblem is not null)
        {
            editor.Notify();
            return;
        }

        var body = new BotScheduleBody(RoutineWords.Cron(When), TimeZone.Trim(), Prompt, ThreadMode, EditedId is not null && editedEnabled);
        FormProblem = await Send(async () =>
        {
            var saved = EditedId is { } id
                ? await api.UpdateBotSchedule(editor.SpaceId, editor.BotId, id, body)
                : await api.CreateBotSchedule(editor.SpaceId, editor.BotId, body);
            Announcement = EditedId is null
                ? $"Routine added, turned off: {RoutineWords.Sentence(saved.Cron, saved.TimeZone)}. Run it once to try it, then turn it on."
                : $"Routine saved: {RoutineWords.Sentence(saved.Cron, saved.TimeZone)}.";
            (Editing, focus) = (false, RoutineFocus.Add);
            return Replace(saved);
        });
        editor.Notify();
    }

    public async Task TurnOn(BotScheduleResponse schedule, bool on)
    {
        var body = new BotScheduleBody(schedule.Cron, schedule.TimeZone, schedule.Prompt, schedule.ThreadMode, on);
        rowProblems.Remove(schedule.Id);

        if (await Send(async () =>
            {
                var saved = await api.UpdateBotSchedule(editor.SpaceId, editor.BotId, schedule.Id, body);
                Announcement = $"{RoutineWords.Sentence(saved.Cron, saved.TimeZone)} is {(saved.Enabled ? "on" : "off")}.";
                return Replace(saved);
            }) is { } problem)
        {
            rowProblems[schedule.Id] = problem;
        }

        editor.Notify();
    }

    /// <summary>
    /// The owner runs their own routine at once. Anyone else first reads the whole prompt they are about to send as
    /// their own message, since they did not write it.
    /// </summary>
    public Task AskToRunOnce(BotScheduleResponse schedule)
    {
        if (schedule.OwnerUserId == session.Current?.UserId)
        {
            return RunOnce(schedule);
        }

        (Running, ConfirmingRun) = (schedule, true);
        editor.Notify();
        return Task.CompletedTask;
    }

    public void SetConfirmingRun(bool value)
    {
        ConfirmingRun = value;
        editor.Notify();
    }

    public async Task RunConfirmed()
    {
        ConfirmingRun = false;

        if (Running is { } schedule)
        {
            Running = null;
            await RunOnce(schedule);

            // The dialog hands focus back to "Run once now", which is disabled while the run is asked for, so the
            // row moves it on once the answer is in: to the conversation it started, or back to the button beside
            // the refusal.
            focusRun = (schedule.Id, focusRun.Ticket + 1);
        }

        editor.Notify();
    }

    public void AskToRemove(BotScheduleResponse schedule)
    {
        (Removing, Confirming) = (schedule, true);
        editor.Notify();
    }

    public void SetConfirming(bool value)
    {
        Confirming = value;
        editor.Notify();
    }

    public async Task Remove()
    {
        if (Removing is not { } schedule)
        {
            return;
        }

        Confirming = false;

        if (await Send(async () =>
            {
                await api.RemoveBotSchedule(editor.SpaceId, editor.BotId, schedule.Id);
                Announcement = $"Routine removed: {RoutineWords.Sentence(schedule.Cron, schedule.TimeZone)}.";
                Editing = Editing && EditedId != schedule.Id;
                focus = RoutineFocus.Add;
                return [.. Schedules.Where(other => other.Id != schedule.Id)];
            }) is { } problem)
        {
            rowProblems[schedule.Id] = problem;
        }

        Removing = null;
        editor.Notify();
    }

    /// <summary>The section was closed: what was being written and what it said go with it, as on the web.</summary>
    public void Reset()
    {
        rowProblems.Clear();
        started.Clear();
        rows.Clear();
        (Editing, EditedId, Prompt, When, TimeZone, ThreadMode, Busy) = (false, null, string.Empty, RoutineWhen.Start, string.Empty, "new_thread", false);
        (Refusal, Next, FormProblem, Announcement, ConfirmingRun, Confirming, Running, Removing) = (null, null, null, null, false, false, null, null);
        (focusRun, focus) = ((null, 0), null);
    }

    #endregion

    #region Private Methods

    private void Open(Guid? id, bool enabled, string text, RoutineWhen chosen, string zone, string mode)
    {
        (Editing, EditedId, editedEnabled, Prompt, When, TimeZone, ThreadMode) = (true, id, enabled, text, chosen, zone, mode);
        (Refusal, Next, FormProblem, focus) = (null, null, null, RoutineFocus.Form);
        editor.Notify();
    }

    private async Task RunOnce(BotScheduleResponse schedule)
    {
        rowProblems.Remove(schedule.Id);

        if (await Send(async () =>
            {
                var run = await api.RunBotScheduleOnce(editor.SpaceId, editor.BotId, schedule.Id);
                started[schedule.Id] = Addresses.Conversation(run.ThreadId);
                Announcement = $"Started once now, as you: {RoutineWords.Sentence(schedule.Cron, schedule.TimeZone)}. Its row links to the conversation.";
                return null;
            }) is { } problem)
        {
            rowProblems[schedule.Id] = problem;
        }

        editor.Notify();
    }

    private IReadOnlyList<BotScheduleResponse> Replace(BotScheduleResponse saved) =>
        [.. Schedules.Where(schedule => schedule.Id != saved.Id).Append(saved).OrderBy(schedule => schedule.Id)];

    /// <summary>Sends one change and passes on the routines it leaves, if any. Returns the server's sentence when it refused.</summary>
    private async Task<string?> Send(Func<Task<IReadOnlyList<BotScheduleResponse>?>> change)
    {
        Busy = true;
        editor.Notify();

        try
        {
            if (await change() is { } changed)
            {
                editor.Schedules = changed;
            }

            return null;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(RoutinesSectionViewModel));
            return Failures.Sentence(exception, "Check that the server is running.");
        }
        finally
        {
            Busy = false;
        }
    }

    #endregion
}
