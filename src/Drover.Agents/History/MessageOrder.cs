namespace Drover.Agents.History;

/// <summary>
/// Puts a conversation in the order a model accepts: every tool result directly after the message that made its
/// call. Storage keeps the order things happened in, and a run stopped in the middle of a call gets that call's
/// result only later, after whatever a person said in between; providers reject a conversation read that way.
/// </summary>
public static class MessageOrder
{
    #region Public Methods

    public static IReadOnlyList<Message> ResultsAfterCalls(IReadOnlyList<Message> messages)
    {
        List<Message> ordered = [];
        Dictionary<Guid, Message> callers = [];

        foreach (var message in messages)
        {
            foreach (var call in message.Parts.OfType<ToolCallPart>())
            {
                callers[call.ToolCallId] = message;
            }

            if (message.Role is not MessageRole.Tool)
            {
                ordered.Add(message);
                continue;
            }

            var late = message.Parts
                .OfType<ToolResultPart>()
                .Where(result => callers.TryGetValue(result.ToolCallId, out var caller) && !IsStillAnswering(ordered, caller))
                .ToList();

            foreach (var group in late.GroupBy(result => callers[result.ToolCallId]))
            {
                ordered.Insert(EndOfResults(ordered, group.Key), WithParts(message, [.. group]));
            }

            if (late.Count == 0)
            {
                ordered.Add(message);
            }
            else if (message.Parts.Except(late).ToList() is { Count: > 0 } rest)
            {
                ordered.Add(WithParts(message, rest));
            }
        }

        return ordered;
    }

    #endregion

    #region Private Methods

    /// <summary>True while nothing but tool results has followed the caller, so a result added now lands in place.</summary>
    private static bool IsStillAnswering(List<Message> ordered, Message caller) =>
        ordered.Skip(ordered.IndexOf(caller) + 1).All(message => message.Role is MessageRole.Tool);

    private static int EndOfResults(List<Message> ordered, Message caller)
    {
        var index = ordered.IndexOf(caller) + 1;

        while (index < ordered.Count && ordered[index].Role is MessageRole.Tool)
        {
            index++;
        }

        return index;
    }

    private static Message WithParts(Message source, IReadOnlyList<MessagePart> parts) => new()
    {
        Id = source.Id,
        SpaceId = source.SpaceId,
        AudienceUserId = source.AudienceUserId,
        ThreadId = source.ThreadId,
        Position = source.Position,
        Role = source.Role,
        RunId = source.RunId,
        Parts = parts,
        CreatedAt = source.CreatedAt,
    };

    #endregion
}
