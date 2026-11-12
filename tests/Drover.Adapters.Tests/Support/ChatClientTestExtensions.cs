namespace Drover.Adapters.Tests.Support;

/// <summary>Calls on a chat client that carry the test's cancellation token.</summary>
public static class ChatClientTestExtensions
{
    #region Public Methods

    public static Task<ChatResponse> Ask(this IModelChatClient client, IEnumerable<ChatMessage> conversation, ChatOptions? options = null) =>
        new ModelChatClientBridge(client).Ask(conversation, options);

    public static Task<ChatResponse> Ask(this IModelChatClient client, string prompt, ChatOptions? options = null) =>
        new ModelChatClientBridge(client).Ask(prompt, options);

    public static IAsyncEnumerable<ChatResponseUpdate> Stream(this IModelChatClient client, string prompt, ChatOptions? options = null) =>
        new ModelChatClientBridge(client).Stream(prompt, options);

    public static Task<List<ChatResponseUpdate>> StreamToEnd(this IModelChatClient client, string prompt, ChatOptions? options = null) =>
        new ModelChatClientBridge(client).StreamToEnd(prompt, options);

    public static Task<ChatResponse> Ask(this IChatClient client, string prompt, ChatOptions? options = null) =>
        client.GetResponseAsync(prompt, options, TestContext.Current.CancellationToken);

    public static Task<ChatResponse> Ask(
        this IChatClient client,
        IEnumerable<ChatMessage> conversation,
        ChatOptions? options = null) =>
        client.GetResponseAsync(conversation, options, TestContext.Current.CancellationToken);

    public static IAsyncEnumerable<ChatResponseUpdate> Stream(
        this IChatClient client,
        string prompt,
        ChatOptions? options = null) =>
        client.GetStreamingResponseAsync(prompt, options, TestContext.Current.CancellationToken);

    public static async Task<List<ChatResponseUpdate>> StreamToEnd(
        this IChatClient client,
        string prompt,
        ChatOptions? options = null)
    {
        List<ChatResponseUpdate> updates = [];

        await foreach (var update in client.Stream(prompt, options))
        {
            updates.Add(update);
        }

        return updates;
    }

    #endregion
}
