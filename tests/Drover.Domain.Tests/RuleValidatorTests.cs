namespace Drover.Domain.Tests;

/// <summary>
/// What a rule must be to be saved. A rule that cannot be enforced as written is refused in words.
/// </summary>
public sealed class RuleValidatorTests
{
    #region Public Methods

    [Fact]
    public void A_rule_from_the_issue_holds()
    {
        RuleCondition[] conditions =
        [
            new() { Kind = RuleConditionKind.ToolIs, Tools = ["email.send"] },
            new() { Kind = RuleConditionKind.NotInList, Argument = "to", Entries = ["example.org"] },
        ];

        RuleValidator.FindProblem("Mail stays inside", RuleCheckpoint.BeforeTool, RuleEffect.Ask, conditions).Should().BeNull();
        RuleValidator.FindProblem("Always", RuleCheckpoint.BeforeReply, RuleEffect.Block, []).Should().BeNull("no condition means always");
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_rule_that_does_not_hold_is_refused_with_the_reason(string what, RuleCheckpoint checkpoint, RuleEffect effect, RuleCondition[] conditions, string expected)
    {
        RuleValidator.FindProblem("Sample rule", checkpoint, effect, conditions).Should().Contain(expected, what);
    }

    public static TheoryData<string, RuleCheckpoint, RuleEffect, RuleCondition[], string> Refused() => new()
    {
        { "too many conditions", RuleCheckpoint.BeforeReply, RuleEffect.Block, [.. Enumerable.Repeat(Holds(), 11)], "at most 10 conditions" },
        { "ask where nobody can be asked", RuleCheckpoint.BeforeReply, RuleEffect.Ask, [], "only before a tool is used" },
        { "redact a person's message", RuleCheckpoint.MessageArrives, RuleEffect.Redact, [Holds()], "already written it" },
        { "redact with nothing to find", RuleCheckpoint.BeforeReply, RuleEffect.Redact, [new() { Kind = RuleConditionKind.RunExceeds, Counter = RunCounter.Tokens, Limit = 1 }], "finds the text to remove" },
        { "a tool condition on a reply", RuleCheckpoint.BeforeReply, RuleEffect.Block, [new() { Kind = RuleConditionKind.ToolIs, Tools = ["x"] }], "belongs to a rule about a tool" },
        { "no tool named", RuleCheckpoint.BeforeTool, RuleEffect.Block, [new() { Kind = RuleConditionKind.ToolIs }], "Name at least one tool" },
        { "an argument on a result", RuleCheckpoint.AfterTool, RuleEffect.Block, [Holds() with { Argument = "body" }], "only before a tool is used" },
        { "a list with no argument", RuleCheckpoint.BeforeTool, RuleEffect.Block, [new() { Kind = RuleConditionKind.NotInList, Entries = ["x"] }], "Name the argument" },
        { "a blank entry", RuleCheckpoint.BeforeReply, RuleEffect.Block, [new() { Kind = RuleConditionKind.TextContains, Entries = ["a", " "] }], "no blank entry" },
        { "a long list", RuleCheckpoint.BeforeReply, RuleEffect.Block, [new() { Kind = RuleConditionKind.TextContains, Entries = [.. Enumerable.Range(0, 51).Select(number => $"v{number}")] }], "Keep it to 50" },
        { "a long pattern", RuleCheckpoint.BeforeReply, RuleEffect.Block, [new() { Kind = RuleConditionKind.TextMatches, Pattern = new string('a', 501) }], "up to 500 characters" },
        { "a pattern that looks behind", RuleCheckpoint.BeforeReply, RuleEffect.Block, [new() { Kind = RuleConditionKind.TextMatches, Pattern = "(?<=a)b" }], "cannot stall" },
        { "a pattern that refers back", RuleCheckpoint.BeforeReply, RuleEffect.Block, [new() { Kind = RuleConditionKind.TextMatches, Pattern = @"(a)\1" }], "cannot stall" },
        { "a pattern that is not one", RuleCheckpoint.BeforeReply, RuleEffect.Block, [new() { Kind = RuleConditionKind.TextMatches, Pattern = "(" }], "cannot be read" },
        { "no built-in kind", RuleCheckpoint.BeforeReply, RuleEffect.Block, [new() { Kind = RuleConditionKind.TextHolds }], "Choose what the text holds" },
        { "hours that are not times", RuleCheckpoint.BeforeReply, RuleEffect.Block, [new() { Kind = RuleConditionKind.TimeIs, From = "late", To = "07:00", TimeZone = "UTC" }], "two times of day" },
        { "hours that cover nothing", RuleCheckpoint.BeforeReply, RuleEffect.Block, [new() { Kind = RuleConditionKind.TimeIs, From = "07:00", To = "07:00", TimeZone = "UTC" }], "cover nothing" },
        { "hours with no zone", RuleCheckpoint.BeforeReply, RuleEffect.Block, [new() { Kind = RuleConditionKind.TimeIs, From = "22:00", To = "07:00", TimeZone = "Mars/Olympus" }], "IANA name" },
        { "a limit below nothing", RuleCheckpoint.BeforeTool, RuleEffect.Stop, [new() { Kind = RuleConditionKind.RunExceeds, Counter = RunCounter.ToolCalls, Limit = -1 }], "a number from 0" },
        { "a kind that does not exist", RuleCheckpoint.BeforeReply, RuleEffect.Block, [new() { Kind = (RuleConditionKind)99 }], "not a kind of condition" },
    };

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void A_rule_needs_a_name(string? name) =>
        RuleValidator.FindProblem(name, RuleCheckpoint.BeforeReply, RuleEffect.Block, []).Should().Contain("Give the rule a name");

    #endregion

    #region Helpers

    private static RuleCondition Holds() => new() { Kind = RuleConditionKind.TextHolds, Builtin = BuiltinPattern.Key };

    #endregion
}
