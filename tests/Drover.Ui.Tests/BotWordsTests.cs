namespace Drover.Ui.Tests;

/// <summary>The pure parts of the bot editor: the draft a person types into. Routines in words are in Bots/RoutineWordsTests.</summary>
public sealed class BotWordsTests
{
    #region Public Methods

    [Fact]
    public void A_draft_differs_only_when_it_would_save_something_else()
    {
        var saved = new BotDraft { Name = "Marshal", Handle = "marshal", Model = "sample" };
        var same = new BotDraft { Name = "Marshal", Handle = "marshal ", Model = "sample", MaxChildren = " " };
        var changed = new BotDraft { Name = "Marshal", Handle = "marshal", Model = "sample", AllowedHosts = "example.com\nexample.org" };

        same.DiffersFrom(saved).Should().BeFalse();
        changed.DiffersFrom(saved).Should().BeTrue();
        changed.ToRequest().SandboxSettings!.AllowedHosts.Should().Equal("example.com", "example.org");
    }

    [Fact]
    public void A_bots_limits_on_its_notes_are_saved_back_unchanged_though_the_editor_does_not_show_them()
    {
        var bot = new BotDetail(
            Guid.CreateVersion7(), "Marshal", "marshal", new BotAvatarBody(null, null), "Sample purpose", "Sample instructions",
            Guid.CreateVersion7(), "sample", new ModelSettingsBody(null, null, null, "off", false),
            new MemorySettingsBody(true, "none", null, null, 300, 4000), "none", new SandboxSettingsBody("off", [], null), [],
            "active", null, 0, new BuildLimitsBody(null, null), new RunLimitsBody(null, null, null), null, null);

        var request = BotDraft.From(bot).ToRequest();

        request.MemorySettings.Should().Be(new MemorySettingsBody(true, "none", null, null, 300, 4000));
    }

    [Fact]
    public void The_limits_summary_says_what_is_in_force_the_bots_own_limit_or_else_its_spaces()
    {
        var space = new BotRunLimitOptions(50, 5_000_000, 49);

        BotSummaries.Limits(new BotDraft(), null).Should().Be("The space's limits", "nothing is claimed before the server has said");
        BotSummaries.Limits(new BotDraft(), space).Should().Be("50 model calls · 5,000,000 tokens · 49 handoffs");
        BotSummaries.Limits(new BotDraft { MaxSteps = " 1 ", MaxDelegations = "0" }, space).Should().Be("1 model call · 5,000,000 tokens · 0 handoffs");
        BotSummaries.Limits(new BotDraft { MaxTokens = "20000" }, null).Should().Be("20,000 tokens");
        BotSummaries.Limits(new BotDraft { MaxTokens = "lots" }, space).Should().Be("50 model calls · 5,000,000 tokens · 49 handoffs", "a half-typed limit is not shown as one");
        new BotDraft { MaxDelegations = "1.5" }.FindProblem().Should().Be("The limit on handoffs is a whole number.");
        new BotDraft { MaxSteps = "7", MaxTokens = "9000", MaxDelegations = "2" }.ToRequest().RunLimits.Should().Be(new RunLimitsBody(7, 9000, 2));
    }

    [Fact]
    public void A_handle_follows_the_name_until_someone_chooses_it()
    {
        // A handle read from the server was not chosen in this editor.
        var fresh = new BotDraft { Name = "New bot", Handle = "new-bot", HandleChosen = false };
        var chosen = new BotDraft { Name = "New bot", Handle = "helper" };

        fresh.Rename("Scout the 2nd!");
        fresh.Rename("Scout");
        chosen.Rename("Scout");
        var emptied = new BotDraft { Name = "Marshal", Handle = "marshal", HandleChosen = false };
        emptied.Rename(" ");

        fresh.Handle.Should().Be("scout");
        chosen.Handle.Should().Be("helper");
        emptied.Handle.Should().Be("marshal", "a name that makes no handle leaves the one there is");
    }

    [Fact]
    public void A_numbered_handle_the_server_made_follows_a_rename_and_a_chosen_one_does_not()
    {
        var second = new BotDraft { Name = "New bot", Handle = "new-bot-2", HandleChosen = false };
        var chosen = new BotDraft { Name = "New bot", Handle = "new-bot-help", HandleChosen = false };
        var emptyName = new BotDraft { Name = "!!!", Handle = "bot-3", HandleChosen = false };
        var notYetMade = new BotDraft();

        second.Rename("Writer");
        chosen.Rename("Writer");
        emptyName.Rename("Scout");
        notYetMade.Rename("Writer");

        second.Handle.Should().Be("writer");
        chosen.Handle.Should().Be("new-bot-help", "a handle that is not the name's with a number was chosen by someone");
        emptyName.Handle.Should().Be("scout", "a name with no letter or digit makes the handle bot, which can be numbered too");
        notYetMade.Handle.Should().Be("writer", "nothing has been chosen yet");
    }

    [Fact]
    public void A_handle_a_person_typed_never_follows_even_when_it_is_spelled_like_one_the_name_made()
    {
        // new-bot-2 on a bot named New bot is spelled like a generated handle, but the person typed it.
        var typed = new BotDraft { Name = "New bot", Handle = "new-bot-2", HandleChosen = false };
        typed.Handle = "new-bot-2";
        var typedExact = new BotDraft { Name = "New bot", Handle = "new-bot", HandleChosen = false };
        typedExact.Handle = "new-bot";

        typed.HandleChosen.Should().BeTrue("setting the handle is a choice");
        typed.Rename("Writer");
        typedExact.Rename("Writer");

        typed.Handle.Should().Be("new-bot-2");
        typedExact.Handle.Should().Be("new-bot", "even the handle the name makes stays once a person has set it");
    }

    [Fact]
    public void A_handle_from_the_server_was_not_chosen_here_and_a_rename_after_a_choice_in_the_same_editor_still_keeps_it()
    {
        var read = BotDraft.From(Stored("New bot", "new-bot-2"));
        read.HandleChosen.Should().BeFalse();

        read.Rename("Writer");

        read.Handle.Should().Be("writer");
        read.HandleChosen.Should().BeFalse("a rename is not a choice");
        read.Handle = "my-writer";
        read.Rename("Author");
        read.Handle.Should().Be("my-writer");
        new BotDraft { Name = "x", Handle = "x" }.HandleChosen.Should().BeTrue();
    }

    [Theory]
    [InlineData("A  B", "a--b", "Scout", "scout")]
    [InlineData("A -B", "a--b", "Scout", "scout")]
    [InlineData("A  B", "a--b-2", "Scout", "scout")]
    [InlineData("A  B", "a-b", "Scout", "scout")]
    [InlineData("A  B", "a---b", "Scout", "a---b")]
    public void A_handle_an_earlier_server_made_with_every_separator_kept_follows_a_rename(string name, string handle, string renamed, string expected)
    {
        var draft = new BotDraft { Name = name, Handle = handle, HandleChosen = false };

        draft.Rename(renamed);

        draft.Handle.Should().Be(expected);
    }

    [Fact]
    public void A_name_longer_than_a_handle_may_be_makes_a_handle_within_the_limit_in_the_draft_and_on_the_server()
    {
        var longName = new string('a', 120);
        var draft = new BotDraft { Name = "New bot", Handle = "new-bot", HandleChosen = false };

        draft.Rename(longName);

        draft.Handle.Should().Be(BotHandles.Stem(longName));
        draft.Handle.Length.Should().BeLessThanOrEqualTo(BotHandles.MaxLength - 8, "room is left for a number");
        draft.Handle.Should().MatchRegex("^[a-z0-9]+(-[a-z0-9]+)*$");
        BotHandles.Follows(draft.Handle, longName).Should().BeTrue();
    }

    [Theory]
    [InlineData("A  B", "a-b")]
    [InlineData("A B", "a-b")]
    [InlineData("  A - B!  ", "a-b")]
    [InlineData("Scout the 2nd!", "scout-the-2nd")]
    [InlineData("!!!", "bot")]
    public void The_server_and_the_client_make_the_same_handle_from_a_name(string name, string handle)
    {
        BotHandles.Stem(name).Should().Be(handle);
    }

    [Theory]
    [InlineData("new-bot", "New bot", true)]
    [InlineData("new-bot-2", "New bot", true)]
    [InlineData("new-bot-12", "New bot", true)]
    [InlineData("a--b", "A  B", true)]
    [InlineData("a--b-3", "A  B", true)]
    [InlineData("a-b-3", "A  B", true)]
    [InlineData("new-bot-1", "New bot", false)]
    [InlineData("new-bot-0", "New bot", false)]
    [InlineData("new-bot-x", "New bot", false)]
    [InlineData("new-bot-2-3", "New bot", false)]
    [InlineData("helper", "New bot", false)]
    [InlineData("new-bots", "New bot", false)]
    [InlineData("", "New bot", true)]
    public void A_handle_follows_a_name_only_when_the_name_made_it(string handle, string name, bool follows) =>
        BotHandles.Follows(handle, name).Should().Be(follows);

    [Fact]
    public void Settings_the_chosen_model_lacks_are_cleared_and_an_unknown_model_keeps_them()
    {
        var draft = new BotDraft { ReasoningEffort = "high", WebSearch = "on", StructuredOutput = true };

        draft.KeepWhatModelSupports(null);
        var kept = (draft.ReasoningEffort, draft.WebSearch, draft.StructuredOutput);
        draft.KeepWhatModelSupports(new ModelCapabilitiesResponse(null, null, true, true, false, false, [], false, false));

        kept.Should().Be(("high", "on", true));
        (draft.ReasoningEffort, draft.WebSearch, draft.StructuredOutput).Should().Be((string.Empty, "off", false));
    }

    #endregion

    #region Private Methods

    private static BotDetail Stored(string name, string handle) => new(
        Guid.Empty, name, handle, new BotAvatarBody(null, null), string.Empty, string.Empty, Guid.Empty, "sample",
        new ModelSettingsBody(null, null, null, "off", false), new MemorySettingsBody(false, "none", null, null), "none",
        new SandboxSettingsBody("off", [], null), [], "active", null, 0, new BuildLimitsBody(null, null),
        new RunLimitsBody(null, null, null), null, null);

    #endregion
}

