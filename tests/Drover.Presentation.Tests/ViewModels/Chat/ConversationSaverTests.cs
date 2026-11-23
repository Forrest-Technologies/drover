namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// Saving a conversation from its menu: the server writes the file, the device saves it, and a toast says how it
/// ended. Every name and value is made up.
/// </summary>
public sealed class ConversationSaverTests : IDisposable
{
    #region Private Fields

    private static readonly Guid thread = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c1");

    private readonly FakeServer server = new();

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    #endregion

    #region Saving

    [Fact]
    public async Task The_file_the_server_wrote_is_handed_to_the_device_under_its_name_and_the_toast_says_so()
    {
        server.AnswerFile("GET", ExportPath, "# Conversation with Sample bot\n", "text/markdown", "Sample bot conversation 2026-01-02.md");
        var (saver, toasts) = await Saver();
        List<(string Name, string Text, string MediaType)> saved = [];

        await saver.Save(thread, ExportFormats.Markdown, (name, text, type) =>
        {
            saved.Add((name, text, type));
            return Task.FromResult(true);
        });

        server.Addresses.Should().Contain($"GET {ExportPath}?format=markdown");
        saved.Should().Equal(("Sample bot conversation 2026-01-02.md", "# Conversation with Sample bot\n", "text/markdown"));
        toasts.Toasts.Single().Should().Match<ToastMessage>(toast =>
            toast.Kind == ToastKind.Success && toast.Message == "Saved Sample bot conversation 2026-01-02.md.");
    }

    [Fact]
    public async Task The_json_format_is_asked_for_by_name()
    {
        server.AnswerFile("GET", ExportPath, "{}", "application/json", "Sample bot conversation 2026-01-02.json");
        var (saver, _) = await Saver();
        string? type = null;

        await saver.Save(thread, ExportFormats.Json, (_, _, mediaType) =>
        {
            type = mediaType;
            return Task.FromResult(true);
        });

        server.Addresses.Should().Contain($"GET {ExportPath}?format=json");
        type.Should().Be("application/json");
    }

    [Fact]
    public async Task A_save_the_person_cancelled_says_nothing()
    {
        server.AnswerFile("GET", ExportPath, "{}", "application/json", "Sample bot conversation 2026-01-02.json");
        var (saver, toasts) = await Saver();

        await saver.Save(thread, ExportFormats.Json, (_, _, _) => Task.FromResult(false));

        toasts.Toasts.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refusal_from_the_server_is_said_and_nothing_is_handed_to_the_device()
    {
        server.Answer("GET", ExportPath, HttpStatusCode.NotFound);
        var (saver, toasts) = await Saver();
        var handed = false;

        await saver.Save(thread, ExportFormats.Markdown, (_, _, _) =>
        {
            handed = true;
            return Task.FromResult(true);
        });

        handed.Should().BeFalse();
        toasts.Toasts.Single().Should().Match<ToastMessage>(toast =>
            toast.Kind == ToastKind.Error && toast.Message.StartsWith("The conversation could not be saved. ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_device_that_refuses_to_save_is_said_in_plain_words()
    {
        server.AnswerFile("GET", ExportPath, "{}", "application/json", "Sample bot conversation 2026-01-02.json");
        var (saver, toasts) = await Saver();

        await saver.Save(thread, ExportFormats.Json, (_, _, _) => throw new DeviceCallFailedException("The platform refused to save the file."));

        toasts.Toasts.Single().Should().Match<ToastMessage>(toast =>
            toast.Kind == ToastKind.Error && toast.Message == "This device did not save the conversation. Try again, or choose another place to save it.");
    }

    [Fact]
    public async Task A_conversation_with_no_thread_yet_has_nothing_to_save_and_asks_nothing()
    {
        var (saver, toasts) = await Saver();

        await saver.Save(null, ExportFormats.Markdown, (_, _, _) => Task.FromResult(true));

        server.Requests.Should().NotContain(request => request.Path.EndsWith("/export", StringComparison.Ordinal));
        toasts.Toasts.Single().Message.Should().Be("Nothing has been said in this conversation yet, so there is nothing to save.");
    }

    [Fact]
    public void The_menu_items_name_the_action_and_the_format()
    {
        ConversationSaver.MarkdownLabel.Should().Be("Save conversation as Markdown");
        ConversationSaver.JsonLabel.Should().Be("Save conversation as JSON");
    }

    #endregion

    #region Private Methods

    private static string ExportPath => $"/api/spaces/{Samples.Session().Spaces[0].Id}/threads/{thread}/export";

    private async Task<(ConversationSaver Saver, ToastService Toasts)> Saver()
    {
        server.Answer("GET", "/api/session", Samples.Session());
        var api = server.CreateApi();
        var session = new SessionState(api, new ShareInbox());
        await session.Load(TestContext.Current.CancellationToken);
        var toasts = new ToastService(TimeProvider.System);
        return (new(api, session, toasts, NullLogger<ConversationSaver>.Instance), toasts);
    }

    #endregion
}
