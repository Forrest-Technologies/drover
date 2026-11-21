namespace Drover.Ui.Chat;

/// <summary>
/// One message as the conversation shows it. <see cref="Streaming"/> is true while a bot's text is still arriving.
/// <see cref="Pending"/> is true for a person's message sent while the bot was working that the bot has not read yet.
/// <see cref="Tools"/> are the bot's tool calls that follow its words, in the order it made them. <see cref="Files"/>
/// are the files the message carries. <see cref="Reasoning"/> is what the bot reasoned before its words, when its model
/// shared that, and <see cref="Sources"/> are the sources its words cite.
/// <see cref="SentBy"/> says, in words, that a schedule sent the message for the person, who did not type it.
/// <see cref="RunId"/> is the run that wrote a bot's message; <see cref="EndsReply"/> is true on the last words of a
/// finished run when the reader can see its usage. <see cref="ActivityRunId"/> links the run's last stored reply to its activity.
/// <see cref="Model"/> is the model that wrote a bot's stored message, when the server kept it.
/// <see cref="Versions"/> switches between the versions of a person's message, or of a reply, where there are several.
/// <see cref="MayEdit"/> and <see cref="MayRegenerate"/> offer a new version of the last message and the last reply.
/// </summary>
public sealed record ChatMessage(string Key, bool FromPerson, string Text, bool Streaming = false, bool Pending = false)
{
    #region Properties

    public IReadOnlyList<ToolCard> Tools { get; init; } = [];

    public string? SentBy { get; init; }

    public IReadOnlyList<SharedFile> Files { get; init; } = [];

    public string? Reasoning { get; init; }

    public IReadOnlyList<SourceLink> Sources { get; init; } = [];

    /// <summary>True while the bot reasons and none of its words have arrived: the message says it is thinking.</summary>
    public bool Thinking => Streaming && Text.Length == 0 && Reasoning is not null;

    public Guid? RunId { get; init; }

    public bool EndsReply { get; init; }

    /// <summary>
    /// True on the last words of a finished run, for everyone who sees them: where the reply's footer stands. Unlike
    /// <see cref="EndsReply"/>, it does not depend on who may read what the reply used.
    /// </summary>
    public bool FinishesReply { get; init; }

    public Guid? ActivityRunId { get; init; }

    public string? Model { get; init; }

    public VersionSwitch? Versions { get; init; }

    public bool MayEdit { get; init; }

    public bool MayRegenerate { get; init; }

    /// <summary>
    /// True for a bot's message that goes on the turn begun by the message before it: the same run, nothing from a
    /// person between. It stands under that message without a head of its own. Set by <see cref="TurnLedger.InTurns"/>.
    /// </summary>
    public bool ContinuesTurn { get; init; }

    /// <summary>
    /// On the first message of a bot's turn, the whole turn's work as one list: its reasoning and its calls in order,
    /// a run of three or more calls of one tool as one step. Null where the work is drawn as cards, as in handed work.
    /// </summary>
    public IReadOnlyList<LedgerStep>? Ledger { get; init; }

    #endregion
}


/// <summary>
/// Where a message or reply stands among the versions of its place: <see cref="Number"/> of <see cref="Count"/>, and
/// the version before and after it to show instead. <see cref="Enabled"/> is false while the bot works, when the
/// server would not switch.
/// </summary>
public sealed record VersionSwitch(int Number, int Count, Guid? Previous, Guid? Next, bool Enabled = true)
{
    #region Public Methods

    /// <summary>The switch for <paramref name="id"/> among <paramref name="versions"/>, or null when it is not one of them.</summary>
    public static VersionSwitch? For(Guid id, IReadOnlyList<Guid> versions)
    {
        var index = versions.ToList().IndexOf(id);
        return index < 0 || versions.Count < 2
            ? null
            : new(index + 1, versions.Count, index > 0 ? versions[index - 1] : null, index < versions.Count - 1 ? versions[index + 1] : null);
    }

    #endregion
}
