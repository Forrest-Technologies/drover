namespace Drover.Adapters.Tests.Agents;

/// <summary>
/// A provider rejects a conversation where a tool call is not followed at once by its result. Storage keeps the order
/// things happened in, so the order the model is given is made here.
/// </summary>
public sealed class MessageOrderTests
{
    #region Private Fields

    private static readonly Guid first = Guid.CreateVersion7();

    private static readonly Guid second = Guid.CreateVersion7();

    #endregion

    #region Tests

    [Fact]
    public void A_conversation_already_in_order_is_left_as_it_is()
    {
        Message[] stored = [User("Look it up"), Calls(first), Results(first), Text(MessageRole.Assistant, "It is 42."), User("Thanks")];

        MessageOrder.ResultsAfterCalls(stored).Should().Equal(stored);
    }

    [Fact]
    public void A_result_stored_after_a_later_message_is_given_right_after_its_call()
    {
        Message[] stored = [User("Write it"), Calls(first), User("Never mind"), Results(first)];

        var ordered = MessageOrder.ResultsAfterCalls(stored);

        Shape(ordered).Should().Equal("user", "assistant", $"tool {first}", "user");
        ordered[^1].Should().BeSameAs(stored[2]);
    }

    [Fact]
    public void Late_results_of_two_calls_each_go_to_their_own_call_and_results_in_place_stay()
    {
        Message[] stored =
        [
            User("One"),
            Calls(first),
            User("Two"),
            Calls(second),
            Results(first, second),
            Text(MessageRole.Assistant, "Done."),
        ];

        Shape(MessageOrder.ResultsAfterCalls(stored)).Should().Equal(
            "user", "assistant", $"tool {first}", "user", "assistant", $"tool {second}", "assistant");
    }

    [Fact]
    public void A_result_whose_call_is_not_in_the_conversation_stays_where_it_is()
    {
        Message[] stored = [User("One"), Results(first), User("Two")];

        MessageOrder.ResultsAfterCalls(stored).Should().Equal(stored);
    }

    #endregion

    #region Helpers

    private static Message User(string text) => Text(MessageRole.User, text);

    private static Message Text(MessageRole role, string text) => new() { Role = role, Parts = [new TextPart(text)] };

    private static Message Calls(params Guid[] ids) => new()
    {
        Role = MessageRole.Assistant,
        Parts = [.. ids.Select(id => new ToolCallPart(id, id.ToString("N"), "test_tool", JsonElement.Parse("{}")))],
    };

    private static Message Results(params Guid[] ids) => new()
    {
        Role = MessageRole.Tool,
        Parts = [.. ids.Select(id => new ToolResultPart(id, id.ToString("N"), false, JsonElement.Parse("true")))],
    };

    private static IEnumerable<string> Shape(IReadOnlyList<Message> messages) =>
        messages.Select(message => message.Role is MessageRole.Tool
            ? $"tool {string.Join(' ', message.Parts.OfType<ToolResultPart>().Select(result => result.ToolCallId))}"
            : message.Role.ToString().ToLowerInvariant());

    #endregion
}
