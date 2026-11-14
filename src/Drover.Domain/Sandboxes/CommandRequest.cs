namespace Drover.Domain.Sandboxes;

/// <summary>
/// A program and its arguments, run without a shell unless <see cref="Shell"/> builds one. Passing arguments as a list
/// is what keeps text from a model out of shell parsing.
/// </summary>
public sealed record CommandRequest
{
    #region Properties

    public required string Program { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(1);

    #endregion

    #region Public Methods

    /// <summary>A command line for a login bash shell. Only for text the caller wrote, never text from a model.</summary>
    public static CommandRequest Shell(string command) =>
        new() { Program = "/bin/bash", Arguments = ["-l", "-c", command] };

    #endregion
}
