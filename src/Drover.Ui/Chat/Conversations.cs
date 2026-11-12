namespace Drover.Ui.Chat;

/// <summary>
/// Turns what the client holds about a thread and its run into what the conversation shows.
/// </summary>
public static class Conversations
{
    #region Private Fields

    private const string PersonRole = "user";

    private const string BotRole = "assistant";

    private const string TextKind = "text";

    #endregion

    #region Public Methods

    /// <summary>
    /// The messages a person reads: what they wrote and what the bot said, including the reply still arriving.
    /// Tool calls and their results are part of the run's activity, not of the conversation, so a message with
    /// no words is left out.
    /// </summary>
    public static IReadOnlyList<ChatMessage> ToChatMessages(this IEnumerable<MessageView> messages) =>
    [
        .. messages
            .Where(message => message.Role is PersonRole or BotRole)
            .Select(message => new ChatMessage(message.Id.ToString(), message.Role is PersonRole, Words(message), message.IsStreaming))
            .Where(message => message.Text.Length > 0),
    ];

    /// <summary>True while the bot works and none of its words have arrived yet.</summary>
    public static bool IsThinking(this RunView run) =>
        run.IsActive && !run.Messages.Any(message => message.IsStreaming && Words(message).Length > 0);

    /// <summary>Why a run ended without an answer and what to do, or nothing when it did not fail.</summary>
    public static string? Problem(this RunView run) => run.Status is "failed"
        ? $"{run.Error?.Message ?? "The bot stopped before it could answer."} Send your message again to retry."
        : null;

    /// <summary>Something the person should know about a run that is not a failure.</summary>
    public static string? Notice(this RunView run) => run.Status switch
    {
        "cancelled" => "You stopped this reply.",
        "waiting" => "The bot asked for an approval, and this version cannot give one yet. Stop the reply to carry on.",
        _ => null,
    };

    #endregion

    #region Private Methods

    private static string Words(MessageView message) =>
        string.Join("\n\n", message.Parts.Where(part => part.Kind is TextKind && !string.IsNullOrWhiteSpace(part.Text)).Select(part => part.Text));

    #endregion
}
