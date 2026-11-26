namespace Drover.Adapters.OpenAICompatible;

/// <summary>What the current Copilot chat request needs on the wire, besides the static editor headers.</summary>
internal readonly record struct CopilotCall(bool FromPerson, bool HasImage)
{
    #region Private Fields

    private static readonly AsyncLocal<CopilotCall?> current = new();

    #endregion

    #region Properties

    public static CopilotCall? Current
    {
        get => current.Value;
        set => current.Value = value;
    }

    public static CopilotCall From(ModelChatRequest request) =>
        new(
            request.Messages is { Count: > 0 } messages && messages[^1].Role == "user",
            HasImage: request.Messages.Any(HoldsImage));

    #endregion

    #region Private Methods

    private static bool HoldsImage(ModelChatMessage message) =>
        message.Contents.Any(content => content is ModelData or ModelUri);

    #endregion
}
