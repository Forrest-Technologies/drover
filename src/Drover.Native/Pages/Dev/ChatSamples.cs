namespace Drover.Native.Pages;

/// <summary>
/// The gallery's samples of the conversation workspace: the crew list, the side chats, a conversation, and the
/// composer. Every name, message, and state here is a sample; nothing is read from a server.
/// </summary>
internal static class ChatSamples
{
    #region Private Fields

    private static readonly Guid lead = Guid.Parse("00000000-0000-7000-8000-000000000011");

    private static readonly Guid helper = Guid.Parse("00000000-0000-7000-8000-000000000012");

    private static readonly Guid second = Guid.Parse("00000000-0000-7000-8000-000000000013");

    private static readonly Guid connection = Guid.Parse("00000000-0000-7000-8000-000000000014");

    private static readonly Guid sampleRun = Guid.Parse("00000000-0000-7000-8000-000000000015");

    private static readonly Guid sampleStrongerRun = Guid.Parse("00000000-0000-7000-8000-000000000016");

    private static readonly BotSummary[] crew =
    [
        new(lead, "Sample lead bot", "sample-lead", "Sample instructions", connection, "sample-model", "active"),
        new(helper, "Sample helper bot", "sample-helper", "Sample instructions", connection, "sample-model", "active", lead),
        new(second, "Sample paused bot", "sample-paused", "Sample instructions", connection, "sample-model", "paused", lead),
    ];

    #endregion

    #region Public Methods

    public static IEnumerable<GallerySection> Sections() =>
    [
        new("Crew list", "Each bot under the one that manages it. A head is lit while its bot works, and a word says what a bot has for you.", () =>
        [
            Wide(new CrewList
            {
                Crew = CrewTree.From(crew),
                SelectedId = lead,
                Working = ImmutableHashSet.Create(helper),
                ForYou = new Dictionary<Guid, Attention> { [lead] = Attention.WaitingForYou, [second] = Attention.New },
                Handoffs = new Dictionary<Guid, Handoff> { [helper] = Handoff.Working },
            }),
        ]),
        new("Side chats", "Separate conversations with the same bot. The open one is framed.", () =>
        [
            Wide(new SideChatList
            {
                Chats =
                [
                    new(lead, "Sample side chat about a draft", "/dev/gallery", "Sample lead bot", BotIdentity.Hue(lead), false),
                    new(helper, "Sample side chat that is working", "/dev/gallery", "Sample lead bot", BotIdentity.Hue(lead), true, Attention.New),
                ],
                SelectedId = lead,
            }),
        ]),
        new("Conversation", "A person's words on their side, a bot speaking from its head. The plate is lit only while the bot writes.", () =>
        [
            Tall(new MessageList { Model = Conversation(writing: true) }),
        ]),
        new("A reply's reasoning and sources", "Reasoning stays closed until opened; while it arrives the bot's head breathes and the section's edge is lit. Sources sit under the words and open outside the app.", () =>
        [
            Fitting(new MessageList { Model = Reasoned() }),
        ]),
        new("A reply's model", "Under a finished reply, the model that wrote it, beside what the reply used.", () =>
        [
            Fitting(new MessageList { Model = WrittenBy() }),
        ]),
        new("A reply that failed", "What failed in a sentence, one way forward, and the server's report for whoever needs it.", () =>
        [
            Wide(new FailureLine { Cause = FailureCause.ModelUnreachable, BotName = "Sample lead bot", Reported = "sample_error: A sample report from the server." }),
        ]),
        new("Composer", "Enter sends. While a bot works, Stop stands where Send was and a line travels the top edge.", () =>
        [
            Wide(new Composer { Model = new() { BotName = "Sample lead bot" } }),
            Wide(new Composer { Model = new() { BotName = "Sample lead bot", Running = true } }),
            Wide(new Composer
            {
                Model = new()
                {
                    BotName = "Sample lead bot",
                    Text = "A sample message with files",
                    Attachments =
                    [
                        new(lead, "sample-notes.txt", 2048, UploadPhase.Ready, 100),
                        new(helper, "sample-upload.pdf", 4096, UploadPhase.Uploading, 40),
                        new(second, "sample-refused.bin", 8192, UploadPhase.Failed, 0, FileRefusal.TooLarge),
                    ],
                },
            }),
        ]),
        new("Choosing a conversation's model", "Behind the plus button, beside Attach, when the bot may answer with more than one model.", () =>
        [
            Wide(new Composer { Model = ChoosingModel() }),
        ]),
    ];

    /// <summary>A sample conversation of the given length, for looking at and for measuring the list.</summary>
    public static MessageListViewModel Conversation(bool writing, int exchanges = 1)
    {
        List<ChatMessage> messages = [];
        for (var index = 0; index < exchanges; index++)
        {
            messages.Add(new($"sample-question-{index}", true, $"Sample question number {index + 1} about the yard."));
            messages.Add(new($"sample-answer-{index}", false, Answer, Streaming: writing && index == exchanges - 1));
        }

        var list = new MessageListViewModel { BotName = "Sample lead bot", BotHue = BotIdentity.Hue(lead), Messages = messages };
        list.Update();
        return list;
    }

    /// <summary>A sample conversation with a reply that reasoned and cited sources, and one whose reasoning is arriving.</summary>
    public static MessageListViewModel Reasoned()
    {
        List<ChatMessage> messages =
        [
            new("sample-reasoned-question", true, "A sample question that needs sources."),
            new("sample-reasoned-answer", false, "A sample answer drawn from two sample sources.")
            {
                Reasoning = "A sample line of reasoning.\n\nA second sample line that weighs the sources.",
                Sources =
                [
                    SourceLink.From("https://sample.example/first", "Sample source with a title")!,
                    SourceLink.From("https://www.sample.example/second", null)!,
                ],
            },
            new("sample-thinking-question", true, "A sample follow-up question."),
            new("sample-thinking-answer", false, string.Empty, Streaming: true) { Reasoning = "A sample line of reasoning, still arriving" },
        ];

        var list = new MessageListViewModel { BotName = "Sample lead bot", BotHue = BotIdentity.Hue(lead), Messages = messages, Thinking = true };
        list.Update();
        return list;
    }

    /// <summary>A sample conversation whose replies each name the model that wrote them.</summary>
    public static MessageListViewModel WrittenBy()
    {
        List<ChatMessage> messages =
        [
            new("sample-model-question", true, "A sample question for the bot's own model."),
            new("sample-model-answer", false, "A sample answer.") { RunId = sampleRun, EndsReply = true, Model = "sample-model" },
            new("sample-stronger-question", true, "A harder sample question."),
            new("sample-stronger-answer", false, "A sample answer from the stronger model.")
            {
                RunId = sampleStrongerRun,
                EndsReply = true,
                Model = "sample-stronger-model",
            },
        ];

        // A sample: the gallery reads no usage, so a reply says it used nothing.
        var list = new MessageListViewModel
        {
            BotName = "Sample lead bot",
            BotHue = BotIdentity.Hue(lead),
            Messages = messages,
            LoadUsage = runId => Task.FromResult(new RunUsageDetail(runId, "USD", new(null, 0, 0), null)),
        };
        list.Update();
        return list;
    }

    /// <summary>A sample composer with its options open, offering the bot's own model and one more.</summary>
    public static ComposerViewModel ChoosingModel()
    {
        var composer = new ComposerViewModel { BotName = "Sample lead bot", OptionsOpen = true };
        composer.ShowModels(new ConversationModelChoice(
            "sample-model",
            "Sample model",
            "sample-stronger-model",
            [new("sample-model", "Sample model"), new("sample-stronger-model", "Sample stronger model")]));
        return composer;
    }

    #endregion

    #region Private Methods

    private static string Answer => """
        A sample answer with **bold words**, `inline code`, and a list:

        - a first sample point
        - a second sample point

        A closing sample sentence.
        """;

    private static Border Wide(FrameworkElement sample) => new() { Child = sample, Width = Tokens.Number("DvDialogWidth") };

    // As tall as a tall sample, and no wider than the page, so a phone shows the sample at its own width.
    private static Border Fitting(FrameworkElement sample) =>
        new() { Child = sample, MaxWidth = Tokens.Number("DvDialogWidth"), Height = Tokens.Number("DvDialogWidth") };

    private static Border Tall(FrameworkElement sample) =>
        new() { Child = sample, Width = Tokens.Number("DvDialogWidth"), Height = Tokens.Number("DvDialogWidth") };

    #endregion
}
