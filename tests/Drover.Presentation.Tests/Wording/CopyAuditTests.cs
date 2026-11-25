namespace Drover.Presentation.Tests.Wording;

/// <summary>
/// The words #891 retired from the work screens, held so they do not come back into the native client's source.
/// The three Tools strings are also changed by #864 and #890, which rewrite ToolConnectionsPage. That one file
/// is left to those issues; every other retired string must be gone from the native and presentation projects.
/// </summary>
public sealed class CopyAuditTests
{
    #region Private Fields

    private static readonly (string Retired, string New)[] copy =
    [
        ("Back to the crew", "Back to conversations"),
        ("Go to the start page", "Back to conversations"),
        ("This bot is not here", "Bot not found"),
        ("This conversation is not here", "Conversation not found"),
        ("This side chat is not here", "Side chat not found"),
        ("See the bots", "Back to bots"),
        ("What this bot is for", "Purpose"),
        ("Open its settings", "Open bot settings"),
        ("This space has no bots yet. An owner or admin of the space can make one.",
            "This space has no bots yet. An owner or admin of the space can create one."),
        ("What the extension is for", "Description"),
        ("Changes here become the form when you go back to it.", "Changes here also change the form."),
        ("How Drover proves who is calling", "How Drover signs in to this server"),
        ("Nothing / A header / A sign-in", "None / Header / Sign-in"),
        ("The words are the same. What the tool accepts as input changed.",
            "The description is the same. The tool's input changed."),
        ("Only the runs that acted for you. An owner or admin sees the whole space.",
            "Only your own runs are shown. An owner or admin sees the whole space."),
    ];

    #endregion

    #region Public Methods

    [Fact]
    public void None_of_the_retired_navigation_and_not_found_strings_remain()
    {
        var sources = Repository.NativeFiles(".cs", ".xaml")
            .Concat(Repository.PresentationFiles(".cs", ".xaml"));
        var found = from file in sources
                    where !file.Path.EndsWith("ToolConnectionsPage.cs", StringComparison.Ordinal)
                    from pair in copy
                    where file.Text.Contains(pair.Retired, StringComparison.Ordinal)
                    select $"{file.Path}: {pair.Retired}";

        found.Should().BeEmpty("the retired words are replaced in every screen this issue decided");
    }

    [Fact]
    public void Every_replacement_is_used_in_the_client()
    {
        var source = string.Join("\n", Repository.NativeFiles(".cs", ".xaml")
            .Concat(Repository.PresentationFiles(".cs", ".xaml"))
            .Select(file => file.Text));

        // The three option labels are separate values in AuthKinds, not the table's slash-separated shorthand.
        foreach (var (_, replacement) in copy.Where(pair => pair.New != "None / Header / Sign-in"))
        {
            source.Should().Contain(replacement, "the new words are in the client");
        }
    }

    #endregion
}
