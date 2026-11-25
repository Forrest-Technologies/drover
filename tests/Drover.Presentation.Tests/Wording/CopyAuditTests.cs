namespace Drover.Presentation.Tests.Wording;

/// <summary>
/// The words #891 retired from the work screens, held so they do not come back into the native client's source.
/// The Tools sign-in wording was retired by #864. The remaining Tools strings are left to #890, which rewrites
/// ToolConnectionsPage; only those are exempt in that one file.
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
        ("The words are the same. What the tool accepts as input changed.",
            "The description is the same. The tool's input changed."),
        ("Only the runs that acted for you. An owner or admin sees the whole space.",
            "Only your own runs are shown. An owner or admin sees the whole space."),
    ];

    // The table's sign-in row holds three separate option labels, each matched as a whole literal or attribute.
    private static readonly string[] retiredOptions = ["Nothing", "A header", "A sign-in"];

    private static readonly string[] newOptions = ["None", "Header", "Sign-in"];

    // Only these words in ToolConnectionsPage are left to the issues that rewrite that file.
    private static readonly string[] otherIssues =
    [
        "Nothing",
        "A header",
        "A sign-in",
        "The words are the same. What the tool accepts as input changed.",
    ];

    #endregion

    #region Public Methods

    [Fact]
    public void None_of_the_retired_navigation_and_not_found_strings_remain()
    {
        var sources = Repository.NativeFiles(".cs", ".xaml")
            .Concat(Repository.PresentationFiles(".cs", ".xaml"));
        var found = from file in sources
                    from pair in copy
                    where !IsLeftToAnotherIssue(pair.Retired, file.Path)
                    where file.Text.Contains(pair.Retired, StringComparison.Ordinal)
                    select $"{file.Path}: {pair.Retired}";
        var optionsFound = from file in sources
                           from option in retiredOptions
                           where !IsLeftToAnotherIssue(option, file.Path)
                           where file.Text.Contains($"\"{option}\"", StringComparison.Ordinal)
                           select $"{file.Path}: {option}";

        found.Concat(optionsFound)
            .Should().BeEmpty("the retired words are replaced in every screen this issue decided");
    }

    [Fact]
    public void Every_replacement_is_used_in_the_client()
    {
        var source = string.Join("\n", Repository.NativeFiles(".cs", ".xaml")
            .Concat(Repository.PresentationFiles(".cs", ".xaml"))
            .Select(file => file.Text));

        foreach (var (_, replacement) in copy)
        {
            source.Should().Contain(replacement, "the new words are in the client");
        }

        foreach (var option in newOptions)
        {
            source.Should().Contain($"\"{option}\"", "the new sign-in options are in the client");
        }
    }

    #endregion

    #region Private Methods

    private static bool IsLeftToAnotherIssue(string retired, string path) =>
        otherIssues.Contains(retired, StringComparer.Ordinal)
        && path.EndsWith("ToolConnectionsPage.cs", StringComparison.Ordinal);

    #endregion
}
