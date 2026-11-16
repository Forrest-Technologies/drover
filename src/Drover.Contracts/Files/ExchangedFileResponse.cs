namespace Drover.Contracts.Files;

/// <summary>
/// A file the caller and a bot exchanged in one of the caller's conversations with it: the file, the conversation and
/// message that carry it, whether that conversation is the main one, whether the bot shared it, and when.
/// </summary>
public sealed record ExchangedFileResponse(
    FileResponse File,
    Guid ThreadId,
    Guid MessageId,
    bool InMainConversation,
    bool FromBot,
    DateTimeOffset SharedAt);
