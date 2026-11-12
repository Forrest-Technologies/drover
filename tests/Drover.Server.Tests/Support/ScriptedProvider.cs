namespace Drover.Server.Tests.Support;

/// <summary>
/// A provider whose answers a test writes. It has a fixed address, like Venice, so a test can run the whole
/// connection flow without the network, and it records the keys it was handed so a test can look for leaks.
/// </summary>
public sealed class ScriptedProvider : IModelClientProvider
{
    #region Private Fields

    public const string ConnectionKind = "scripted";

    private readonly List<string?> keys = [];

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    public Uri? FixedAddress { get; } = new("https://scripted.example.test/v1/");

    public Func<string?, Task<ConnectionTestResult>> OnTest { get; set; } =
        _ => Task.FromResult(ConnectionTestResult.Ok("Connected."));

    public Func<string?, Task<IReadOnlyList<ModelInfo>>> OnList { get; set; } = _ => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

    public IReadOnlyList<string?> KeysSeen
    {
        get
        {
            lock (keys)
            {
                return [.. keys];
            }
        }
    }

    #endregion

    #region Interface Implementations

    public IModelChatClient CreateChatClient(ModelEndpoint connection, string? apiKey, string modelId, ModelSettings settings) =>
        throw new NotSupportedException("The scripted provider does not chat.");

    public Task<IReadOnlyList<ModelInfo>> ListModels(ModelEndpoint connection, string? apiKey, CancellationToken cancellation)
    {
        Remember(apiKey);
        return OnList(apiKey);
    }

    public Task<ConnectionTestResult> TestConnection(ModelEndpoint connection, string? apiKey, CancellationToken cancellation)
    {
        Remember(apiKey);
        return OnTest(apiKey);
    }

    #endregion

    #region Private Methods

    private void Remember(string? apiKey)
    {
        lock (keys)
        {
            keys.Add(apiKey);
        }
    }

    #endregion
}
