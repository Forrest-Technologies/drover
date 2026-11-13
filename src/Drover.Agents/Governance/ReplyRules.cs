namespace Drover.Agents.Governance;

/// <summary>
/// Chat-client middleware for a bot's rules about its replies. It sits between the framework's tool loop and the
/// model, so it sees each model call's answer before the framework stores it and before the run streams it on.
/// When the bot has a rule about replies, the answer is held back until it is complete: nothing a rule forbids is
/// shown, even for a moment, and nothing of it is stored. A bot with no such rule streams exactly as before.
/// The governor decides; this class only holds the text and puts back what the governor allows.
/// </summary>
public sealed class ReplyRules(IChatClient inner, IRunGovernor governor) : DelegatingChatClient(inner)
{
    #region Overrides

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        if (!await governor.HoldsReplies(cancellationToken))
        {
            return response;
        }

        foreach (var message in response.Messages)
        {
            message.Contents = await Rule(message.Contents, cancellationToken);
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!await governor.HoldsReplies(cancellationToken))
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                yield return update;
            }

            yield break;
        }

        // Held back: the whole answer is collected first, so a rule sees complete text and decides before any of
        // it leaves this server.
        List<ChatResponseUpdate> held = [];

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            held.Add(update);
        }

        if (held.Count == 0)
        {
            yield break;
        }

        // One update carries everything that is said; the others keep what is not text (tool calls, usage) and
        // their place.
        var said = await Rule([.. held.SelectMany(update => update.Contents).Where(IsSaid)], cancellationToken);
        var first = held[0];
        yield return new ChatResponseUpdate(first.Role, said)
        {
            MessageId = first.MessageId,
            ResponseId = first.ResponseId,
            ModelId = first.ModelId,
            CreatedAt = first.CreatedAt,
            AuthorName = first.AuthorName,
        };

        foreach (var update in held)
        {
            update.Contents = [.. update.Contents.Where(content => !IsSaid(content))];
            yield return update;
        }
    }

    #endregion

    #region Private Methods

    private static bool IsSaid(AIContent content) => content is TextContent or TextReasoningContent;

    // Text and reasoning are each ruled on as one whole, so a match split across pieces is still a match. What is
    // not said, such as a tool call, is left for the tool gate.
    private async Task<IList<AIContent>> Rule(IList<AIContent> contents, CancellationToken cancellationToken)
    {
        var text = string.Concat(contents.OfType<TextContent>().Select(content => content.Text));
        var reasoning = string.Concat(contents.OfType<TextReasoningContent>().Select(content => content.Text));
        List<AIContent> ruled = [];

        if (reasoning.Length > 0)
        {
            ruled.Add(new TextReasoningContent(await governor.RuleOnReply(reasoning, cancellationToken)));
        }

        if (text.Length > 0)
        {
            ruled.Add(new TextContent(await governor.RuleOnReply(text, cancellationToken)));
        }

        return [.. ruled, .. contents.Where(content => !IsSaid(content))];
    }

    #endregion
}
