namespace Drover.Agents.Governance;

/// <summary>
/// Chat-client middleware that shows the model the picture a tool just made, such as a screenshot. A tool result in
/// the chat completions protocol is text only, so the picture goes in a user message after the results, on the next
/// model call alone. This client sits below the framework's per-call history persistence, which keeps the list of
/// messages it was handed, not the new one made here, so the picture is never stored: after a restart the model sees
/// the tool's JSON result and takes a new screenshot.
/// </summary>
public sealed class TransientImages(IChatClient inner, RunContext run) : DelegatingChatClient(inner)
{
    #region Overrides

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(WithLatestImage(messages), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(WithLatestImage(messages), options, cancellationToken);

    #endregion

    #region Private Methods

    /// <summary>The call's messages, and after them the waiting picture, in a new list the caller never sees.</summary>
    private IEnumerable<ChatMessage> WithLatestImage(IEnumerable<ChatMessage> messages) =>
        run.TakeImageForModelCall() is { } shown
            ?
            [
                .. messages,
                new ChatMessage(
                    ChatRole.User,
                    [
                        new TextContent($"The image from the {shown.ToolName} call {shown.CallId}. It is shown once and not kept."),
                        new DataContent(shown.Image.Data, shown.Image.MediaType),
                    ]),
            ]
            : messages;

    #endregion
}
