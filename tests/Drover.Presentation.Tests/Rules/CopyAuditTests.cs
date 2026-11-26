namespace Drover.Presentation.Tests.Rules;

/// <summary>
/// The retired setup, model, bot, tool and extension words from #864 and the retired conversation, search, usage
/// and memory words from #865, held so they do not come back into the client's source. Only the native and
/// presentation projects are in scope; the engine's server sentences are not.
/// </summary>
public sealed class CopyAuditTests
{
    #region Private Fields

    private static readonly string[] retired =
    [
        "Bots think with a model you bring",
        "Meet your first bot",
        "Your first bot leads the crew",
        "It leads your crew",
        "A bot needs a model to think with",
        "Thinks with",
        "How Drover proves who is calling",
        "These stop a bot that runs away",
        "Drover has not worked out the next time yet",
        "Write a bot down so someone else can import it",
        "Giving the tools",
        "the ticked tools, asking first",
        "Which bot gets this?",
        "Notifications arriving late?",
    ];

    private static readonly string[] retiredConversation =
    [
        "Words someone said",
        "Enter text to search",
        "Adding it up.",
        "Written by {Model}",
        "See what happened",
        "What {botName} did",
        "What {botName} thought",
        "What happened in this run of {Bot.Name}",
        "This run's events are no longer kept. The conversation still has what was said.",
        "What {Shown!.Name} and its crew did in a run",
        "What {Shown!.Name} remembers about you",
        "What {BotName} remembers about you",
        "Loading what {BotName} remembers.",
        "Something {BotName} should know about you from now on.",
        "Nothing said yet. Write to {BotName} below.",
        "Nothing has been said in this conversation yet, so there is nothing to save.",
        "A notification names the bot and what happened. It never shows what was said.",
        "{BotName} needs you at its screen",
        "See what it needs.",
        "What got in the way (optional, {BotName} reads it)",
        "Asking for you at the screen",
        "Asked for you at the screen",
        "Looking over its crew",
        "Looked over its crew",
        "Drover could not add up what the bots used",
        "Adding up what each bot used.",
        "Finding the runs that cost the most.",
    ];

    #endregion

    #region Public Methods

    [Fact]
    public void None_of_the_retired_setup_and_bot_strings_remain()
    {
        var sources = Repository.NativeFiles(".cs", ".xaml")
            .Concat(Repository.PresentationFiles(".cs", ".xaml"));
        var found = from file in sources
                    from phrase in retired
                    where file.Text.Contains(phrase, StringComparison.Ordinal)
                    select $"{file.Path}: {phrase}";

        found.Should().BeEmpty("the plain words from #864 are used in every screen this issue decided");
    }

    [Fact]
    public void None_of_the_retired_conversation_strings_remain()
    {
        var sources = Repository.NativeFiles(".cs", ".xaml")
            .Concat(Repository.PresentationFiles(".cs", ".xaml"));
        var found = from file in sources
                    from phrase in retiredConversation
                    where file.Text.Contains(phrase, StringComparison.Ordinal)
                    select $"{file.Path}: {phrase}";

        found.Should().BeEmpty("the plain words from #865 are used in every screen this issue decided");
    }

    #endregion
}
