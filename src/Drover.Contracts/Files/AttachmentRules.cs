namespace Drover.Contracts.Files;

/// <summary>
/// What a conversation's bot can take, so the composer can say before sending why a file cannot go. The server
/// decides again when the file is uploaded and when the message is sent.
/// </summary>
public sealed record AttachmentRules(long MaxFileBytes, int MaxFilesPerMessage, bool ModelReadsImages, bool HasSandbox);
