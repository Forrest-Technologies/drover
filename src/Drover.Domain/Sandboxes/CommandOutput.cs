namespace Drover.Domain.Sandboxes;

/// <summary>One piece of a running command's output, in the order the provider sent it.</summary>
public abstract record CommandOutput;

public sealed record CommandStarted(int Pid) : CommandOutput;

public sealed record CommandStdout(ReadOnlyMemory<byte> Data) : CommandOutput;

public sealed record CommandStderr(ReadOnlyMemory<byte> Data) : CommandOutput;

/// <summary>The last output of a command. <paramref name="Error"/> is the provider's note on how it ended, if any.</summary>
public sealed record CommandExited(int ExitCode, string? Error) : CommandOutput;
