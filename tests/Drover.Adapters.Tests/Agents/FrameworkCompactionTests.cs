namespace Drover.Adapters.Tests.Agents;

public sealed class FrameworkCompactionTests
{
    [Fact]
    public async Task Tool_eviction_removes_call_ids_from_the_outgoing_copy()
    {
        ChatMessage[] history =
        [
            new(ChatRole.User, "First"),
            new(ChatRole.Assistant, [new FunctionCallContent("id", "lookup", new Dictionary<string, object?>())]),
            new(ChatRole.Tool, [new FunctionResultContent("id", new string('x', 5000))]),
            new(ChatRole.User, "Continue"),
        ];
        var strategy = new Microsoft.Agents.AI.Compaction.ToolResultCompactionStrategy(Microsoft.Agents.AI.Compaction.CompactionTriggers.Always, 1);
        var compact = await Microsoft.Agents.AI.Compaction.CompactionProvider.CompactAsync(strategy, history, cancellationToken: TestContext.Current.CancellationToken);

        compact.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Should().BeEmpty();
        history.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Should().ContainSingle();
    }
}
