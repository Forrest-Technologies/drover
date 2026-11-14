namespace Drover.Domain.Sandboxes;

public sealed record SandboxEntry(string Name, string Path, SandboxEntryKind Kind, long Size, DateTimeOffset? ModifiedAt);
