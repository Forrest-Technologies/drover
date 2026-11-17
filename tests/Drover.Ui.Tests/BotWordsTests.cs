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
            "active", null, 0, new BuildLimitsBody(null, null), null, null);

        var request = BotDraft.From(bot).ToRequest();

        request.MemorySettings.Should().Be(new MemorySettingsBody(true, "none", null, null, 300, 4000));
    }

    [Fact]
    public void A_handle_follows_the_name_until_someone_chooses_it()
    {
        var fresh = new BotDraft { Name = "New bot", Handle = "new-bot" };
        var chosen = new BotDraft { Name = "New bot", Handle = "helper" };

        fresh.Rename("Scout the 2nd!");
        fresh.Rename("Scout");
        chosen.Rename("Scout");
        var emptied = new BotDraft { Name = "Marshal", Handle = "marshal" };
        emptied.Rename(" ");

        fresh.Handle.Should().Be("scout");
        chosen.Handle.Should().Be("helper");
        emptied.Handle.Should().Be("marshal", "a name that makes no handle leaves the one there is");
    }

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
}
