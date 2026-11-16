namespace Drover.Contracts.Files;

/// <summary>
/// What a conversation's bot can take, so the composer can say before sending why a file cannot go. The server
/// decides again when the file is uploaded and when the message is sent. <see cref="HasSandbox"/> is whether files
/// can go to the bot's sandbox from this conversation: never from a private one in a space where others can reach the
/// sandbox.
/// </summary>
public sealed record AttachmentRules(long MaxFileBytes, int MaxFilesPerMessage, bool ModelReadsImages, bool HasSandbox);
