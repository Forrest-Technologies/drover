namespace Drover.Presentation.ViewModels.Chat;

/// <summary>One part of the status line. A number is drawn in the machine font.</summary>
public sealed record StatusPart(string Text, bool IsNumber = false);

/// <summary>What a conversation holds now, as far as its status line says it.</summary>
/// <param name="Messages">What was said.</param>
/// <param name="Working">True while a run of the conversation is under way.</param>
/// <param name="Thinking">True while the bot works and has not started to answer.</param>
/// <param name="Waiting">True while the run waits for the person's answer.</param>
/// <param name="Model">The model the conversation answers with, when it is known.</param>
/// <param name="ModelIsChosen">True when that model was chosen for this conversation only.</param>
public sealed record ConversationStatusFacts(
    IReadOnlyList<ChatMessage> Messages,
    bool Working = false,
    bool Thinking = false,
    bool Waiting = false,
    string? Model = null,
    bool ModelIsChosen = false);

/// <summary>
/// The one quiet line above a conversation's message field (docs/ux/conversation-display.md, section 1). While a run
/// works: what the bot is doing, then the model. At rest: the model that wrote the newest reply, then the tokens and
/// the cost of that reply once they are read. A part that is not known is left out, never shown as zero. What the
/// newest reply used is read once, when the line first shows that reply, and the line then opens the whole of it.
/// A read that fails leaves the figures out; the popover says what failed. With nothing to say the line is not there.
/// </summary>
public sealed class ConversationStatusViewModel : ObservableObject
{
    #region Private Fields

    /// <summary>What stands between two parts of the line.</summary>
    public const string Separator = " · ";

    public const string WaitingForYou = "Waiting for you";

    public const string OpensUsage = "Shows usage for this reply";

    private Guid? run;

    private ReplyUsageViewModel? read;

    private bool working;

    #endregion

    #region Properties

    /// <summary>The parts of the line, in the order they stand. Empty when the line has nothing to say.</summary>
    public IReadOnlyList<StatusPart> Parts { get; private set; } = [];

    /// <summary>What the newest finished reply used, when this person may read it: the line then opens it.</summary>
    public ReplyUsageViewModel? Usage => working ? null : read;

    /// <summary>False when the line has nothing to say: it then takes no room.</summary>
    public bool HasAny => Parts.Count > 0;

    /// <summary>The line as it is drawn when all of it fits.</summary>
    public string Line => string.Join(Separator, Parts.Select(part => part.Text));

    /// <summary>
    /// The whole line as a sentence, for someone who cannot see it, whatever part of it fits the screen:
    /// "vendor/model, 1,240 tokens, USD 0.02. Shows usage for this reply."
    /// </summary>
    public string Name => !HasAny ? string.Empty
        : Usage is null ? $"{string.Join(", ", Parts.Select(part => part.Text))}."
        : $"{string.Join(", ", Parts.Select(part => part.Text))}. {OpensUsage}.";

    #endregion

    #region Public Methods

    /// <summary>Says what the conversation now holds. A reply the line shows for the first time has its usage read.</summary>
    public void Show(ConversationStatusFacts facts, Func<Guid, Task<RunUsageDetail>>? load, ILogger logger)
    {
        working = facts.Working;
        var reply = ChatRows.NewestFinished(facts.Messages);
        Follow(reply is not null && ChatRows.OffersUsage(reply, load is not null) ? reply : null, load, logger);
        Parts = working ? WorkingParts(facts) : RestParts(reply, facts);
        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// The state a working run is in, in the words the conversation already says it with: waiting for the person, what
    /// an arriving reply says, the tool that is running, or thinking. Nothing when none of these is known.
    /// </summary>
    public static string? State(ConversationStatusFacts facts) =>
        facts.Waiting ? WaitingForYou
        : facts.Messages.LastOrDefault(message => message.Streaming) is { } arriving ? ChatRows.Arriving(arriving)
        : Running(facts.Messages) is { } tool ? tool.Label
        : facts.Thinking ? TurnLedger.Thinking
        : null;

    #endregion

    #region Private Methods

    private static ToolCard? Running(IReadOnlyList<ChatMessage> messages) =>
        messages.SelectMany(message => message.Tools).LastOrDefault(tool => tool.Phase is ToolPhase.Running);

    // One view model for a run, kept while the line shows that run: the reply's words arrive again from the server
    // with more known about them, and what was read, or is being read, must not be lost with the first of them.
    private void Follow(ChatMessage? reply, Func<Guid, Task<RunUsageDetail>>? load, ILogger logger)
    {
        if (reply?.RunId == run)
        {
            read?.Model = reply?.Model;
            return;
        }

        read?.PropertyChanged -= UsageChanged;
        run = reply?.RunId;
        read = reply is { RunId: { } runId } && load is not null ? new(runId, load, logger, reply.Model) : null;

        if (read is not null)
        {
            read.PropertyChanged += UsageChanged;
            _ = read.Read();
        }
    }

    private void UsageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(ReplyUsageViewModel.Detail) && !working && Parts.Count > 0)
        {
            Parts = [.. Parts.Where(part => !part.IsNumber), .. Figures(read?.Detail)];
            OnPropertyChanged(string.Empty);
        }
    }

    private static List<StatusPart> WorkingParts(ConversationStatusFacts facts)
    {
        List<StatusPart> parts = [];

        if (State(facts) is { } state)
        {
            parts.Add(new(state));
        }

        if (ModelWords(facts) is { } model)
        {
            parts.Add(new(model));
        }

        return parts;
    }

    // At rest the model is the one that wrote the newest reply. Before any reply it is the conversation's own.
    private List<StatusPart> RestParts(ChatMessage? reply, ConversationStatusFacts facts)
    {
        var model = reply is null ? ModelWords(facts) : reply.Model;
        List<StatusPart> parts = model is null ? [] : [new(model)];
        parts.AddRange(Figures(read?.Detail));
        return parts;
    }

    private static string? ModelWords(ConversationStatusFacts facts) => facts switch
    {
        { Model: null or "" } => null,
        { ModelIsChosen: true } => $"{facts.Model}, for this conversation",
        _ => facts.Model,
    };

    // The reply with the bots it asked when there were any, since that is what it cost. A price the server does not
    // know is left out, never shown as nothing.
    private static IEnumerable<StatusPart> Figures(RunUsageDetail? read)
    {
        if (read is null)
        {
            yield break;
        }

        var figure = read.Tree ?? read.Own;
        yield return new($"{UsageWords.Tokens(figure.InputTokens + figure.OutputTokens)} tokens", IsNumber: true);

        if (figure.Cost is { } cost)
        {
            yield return new(UsageWords.Money(cost, read.Currency), IsNumber: true);
        }
    }

    #endregion
}
