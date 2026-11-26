namespace Drover.Presentation.Tests.Rules;

/// <summary>
/// The retired setup, model, bot, tool and extension words from #864, held so they do not come back into the
/// client's source. Only the native and presentation projects are in scope; the engine's server sentences are not.
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

    #endregion
}
