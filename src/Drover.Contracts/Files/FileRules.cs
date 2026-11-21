namespace Drover.Contracts.Files;

/// <summary>The server's file size limit for uploads in a space, so screens agree with the server before uploading.</summary>
public sealed record FileRules(long MaxFileBytes);
