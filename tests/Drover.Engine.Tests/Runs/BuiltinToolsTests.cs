namespace Drover.Engine.Tests.Runs;

public sealed class BuiltinToolsTests
{
    #region Private Fields

    private static readonly DateTimeOffset moment = new(2026, 11, 12, 15, 30, 0, TimeSpan.Zero);

    #endregion

    #region Public Methods

    [Fact]
    public async Task Time_now_gives_utc_and_the_named_time_zone()
    {
        var tool = BuiltinTools.TimeNow(new FixedTime(moment));

        var plain = await tool.Run(Call("{}"), CancellationToken.None);
        var zoned = await tool.Run(Call("""{"time_zone":"Europe/Berlin"}"""), CancellationToken.None);
        var unknown = await tool.Run(Call("""{"time_zone":"Nowhere/Land"}"""), CancellationToken.None);

        plain.GetProperty("utc").GetString().Should().Be("2026-11-12T15:30:00.0000000+00:00");
        zoned.GetProperty("local").GetString().Should().Be("2026-11-12T16:30:00.0000000+01:00");
        zoned.GetProperty("time_zone").GetString().Should().Be("Europe/Berlin");
        unknown.GetProperty("error").GetString().Should().Contain("Nowhere/Land");
    }

    [Fact]
    public void A_built_in_tool_reads_only_and_its_model_name_has_no_dot()
    {
        var tool = BuiltinTools.TimeNow(TimeProvider.System);

        tool.Sensitivity.Should().Be(ToolSensitivity.Read);
        tool.Name.Should().Be("time.now");
        BuiltinTool.ModelName(tool.Name).Should().Be("time_now");
        tool.DefinitionHash.Should().HaveLength(64);
    }

    #endregion

    #region Private Methods

    private static BuiltinToolCall Call(string arguments) =>
        new(
            JsonElement.Parse(arguments),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new RunTenant(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Guid.CreateVersion7(), Guid.CreateVersion7()),
            _ => Task.CompletedTask,
            () => Task.CompletedTask);

    #endregion

    #region Nested Types

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    #endregion
}
