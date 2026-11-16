namespace Drover.Domain.Agents;

/// <summary>One note as a run is given it: the bot's own words, and when they were last written.</summary>
public sealed record RecalledNote(Guid Id, string Text, DateTimeOffset ChangedAt);


/// <summary>
/// The notes a bot kept about the person its run acts for, oldest first, and the most characters of them the run is
/// given. They are reference material the bot wrote earlier, never an instruction.
/// </summary>
public sealed record RecalledMemory(IReadOnlyList<RecalledNote> Notes, int MaxCharacters)
{
    #region Properties

    /// <summary>A note was written by a tainted run, so a run given these notes is tainted from its start.</summary>
    public bool Tainted { get; init; }

    /// <summary>The run may call memory_list, so it can be pointed there for the notes that did not fit.</summary>
    public bool CanList { get; init; }

    #endregion
}
