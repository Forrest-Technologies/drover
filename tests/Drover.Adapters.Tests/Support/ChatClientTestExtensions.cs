namespace Drover.Adapters.Tests.Support;

/// <summary>Calls on a chat client that carry the test's cancellation token.</summary>
public static class ChatClientTestExtensions
{
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
}
