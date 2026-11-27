namespace Drover.Engine.Memory;

/// <summary>
/// Configuration for the optional external memory service at <c>Drover:Memory</c>, validated at startup.
/// A provider's own settings live in its adapter.
/// </summary>
public sealed class MemoryOptions
{
    #region Properties

    /// <summary>The configured memory provider kind. Null means no memory service is configured.</summary>
    public string? Provider { get; set; }

    /// <summary>The most characters of recalled memory service text given to a run.</summary>
    public int RecallMaxCharacters { get; set; } = DefaultRecallMaxCharacters;

    /// <summary>How many seconds recall waits on the memory service before giving up and letting the run go on.</summary>
    public int RecallTimeoutSeconds { get; set; } = DefaultRecallTimeoutSeconds;

    #endregion

    #region Constants

    public const string Section = "Drover:Memory";

    public const int DefaultRecallMaxCharacters = 4000;

    public const int DefaultRecallTimeoutSeconds = 5;

    #endregion
}


/// <summary>Validates <see cref="MemoryOptions"/> at server startup so invalid limits fail fast.</summary>
public sealed class MemoryOptionsValidator : IValidateOptions<MemoryOptions>
{
    #region Interface Implementations

    public ValidateOptionsResult Validate(string? name, MemoryOptions options)
    {
        List<string> failures = [];

        if (options.RecallMaxCharacters <= 0)
        {
            failures.Add($"{MemoryOptions.Section}:{nameof(MemoryOptions.RecallMaxCharacters)} must be greater than zero.");
        }

        if (options.RecallTimeoutSeconds <= 0)
        {
            failures.Add($"{MemoryOptions.Section}:{nameof(MemoryOptions.RecallTimeoutSeconds)} must be greater than zero.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    #endregion
}
