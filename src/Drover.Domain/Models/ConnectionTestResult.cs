namespace Drover.Domain.Models;

/// <summary>The outcome of testing a connection. Balances are filled when the provider reports them.</summary>
public sealed record ConnectionTestResult(
    bool Succeeded,
    string Message,
    ModelFailure? Failure = null,
    IReadOnlyList<ModelBalance>? Balances = null)
{
    public static ConnectionTestResult Ok(string message, IReadOnlyList<ModelBalance>? balances = null) =>
        new(true, message, Balances: balances);

    public static ConnectionTestResult Failed(ModelFailure failure, string message) => new(false, message, failure);
}
