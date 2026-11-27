namespace Drover.Adapters.Tests.Support;

/// <summary>
/// Settings a test can change between two requests, as an operator changes configuration on a running server: the
/// adapter reads <see cref="CurrentValue"/> for each request.
/// </summary>
public sealed class SettableOptions<T>(T value) : IOptionsMonitor<T>
{
    #region Properties

    public T CurrentValue { get; set; } = value;

    #endregion

    #region Interface Implementations

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;

    #endregion
}
