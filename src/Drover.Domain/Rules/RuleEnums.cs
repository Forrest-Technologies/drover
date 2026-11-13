namespace Drover.Domain.Rules;

/// <summary>The moment in a run at which a rule is checked.</summary>
public enum RuleCheckpoint
{
    /// <summary>The bot is about to use a tool. The arguments are known; the tool has not run.</summary>
    BeforeTool,

    /// <summary>A tool has answered. Its result has not reached the model.</summary>
    AfterTool,

    /// <summary>The bot's reply is complete and has not been shown or stored.</summary>
    BeforeReply,

    /// <summary>A person's message has arrived and the model has not seen it.</summary>
    MessageArrives,
}

/// <summary>What a rule does when it applies. The order is the order of strictness: a later value wins.</summary>
public enum RuleEffect
{
    /// <summary>The matched text is replaced, and the run goes on.</summary>
    Redact,

    /// <summary>A person is asked first, through the approval flow.</summary>
    Ask,

    /// <summary>The thing is not done, and the bot is told why.</summary>
    Block,

    /// <summary>The run ends.</summary>
    Stop,
}

/// <summary>What one condition of a rule looks at.</summary>
public enum RuleConditionKind
{
    /// <summary>The tool is one of the named tools.</summary>
    ToolIs,

    /// <summary>The tool reads, writes, or is critical, as Drover has it stored.</summary>
    ToolKind,

    /// <summary>The text contains one of the given pieces of text.</summary>
    TextContains,

    /// <summary>The text matches a regular expression.</summary>
    TextMatches,

    /// <summary>A value of the named argument is not on the list.</summary>
    NotInList,

    /// <summary>The text holds something of a built-in kind, such as a card number.</summary>
    TextHolds,

    /// <summary>The time of day and the weekday, in the condition's time zone.</summary>
    TimeIs,

    /// <summary>The run has used more tool calls or tokens than a number.</summary>
    RunExceeds,
}

/// <summary>The kinds of text a rule can look for without a pattern being written.</summary>
public enum BuiltinPattern
{
    EmailAddress,
    CardNumber,
    Key,
}

/// <summary>What a run counts that a rule can put a limit on.</summary>
public enum RunCounter
{
    ToolCalls,
    Tokens,
}
