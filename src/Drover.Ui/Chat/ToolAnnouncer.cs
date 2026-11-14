namespace Drover.Ui.Chat;

/// <summary>
/// Decides what a screen reader hears about tool calls: one sentence each time a call's state changes, never one per
/// piece of output. A call first seen when it has already ended is history and is not read out; a call first seen
/// while the bot works on it, or while it waits for an answer, is.
/// </summary>
public sealed class ToolAnnouncer
{
    #region Private Fields

    private readonly Dictionary<Guid, ToolPhase> heard = [];

    #endregion

    #region Public Methods

    /// <summary>What changed since the last call, as sentences, or nothing when no call changed.</summary>
    public string? Next(IEnumerable<ToolCard> cards, string botName)
    {
        List<string> sentences = [];

        foreach (var card in cards)
        {
            var phase = Spoken(card.Phase);
            var known = heard.TryGetValue(card.Id, out var before);
            heard[card.Id] = phase;

            if (known ? before != phase : phase is ToolPhase.Running or ToolPhase.WaitingForYou)
            {
                sentences.Add(Sentence(card, botName));
            }
        }

        return sentences.Count == 0 ? null : string.Join(" ", sentences);
    }

    #endregion

    #region Private Methods

    // Starting, approved, and running are one thing to a listener: the bot is at work on the call.
    private static ToolPhase Spoken(ToolPhase phase) =>
        phase is ToolPhase.Starting or ToolPhase.Approved ? ToolPhase.Running : phase;

    private static string Sentence(ToolCard card, string botName) => card.Phase switch
    {
        ToolPhase.WaitingForYou => $"{botName} asks to {card.Request}. Waiting for you.",
        _ when card.IsLive => $"{card.Label}.",
        _ => $"{card.Label}. {card.State}.",
    };

    #endregion
}
