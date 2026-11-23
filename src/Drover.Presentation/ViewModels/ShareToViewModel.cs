namespace Drover.Presentation.ViewModels;

/// <summary>
/// The share screen. Its job: choose which bot's conversation gets what another app shared, as a draft. The share
/// waits in the one <see cref="ShareInbox"/> both clients use; choosing a bot names it there, and that bot's main
/// conversation takes the share when it opens. Nothing is sent until the person sends it.
/// </summary>
public sealed partial class ShareToViewModel(
    DroverApi api,
    SessionState session,
    ShareInbox inbox,
    Navigator navigator,
    ILogger<ShareToViewModel> logger) : ObservableObject
{
    #region Private Fields

    private const int ExcerptLength = 400;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNothing), nameof(ShowShared), nameof(ShowFailed), nameof(ShowLoading), nameof(ShowNoBots), nameof(ShowBots))]
    [NotifyPropertyChangedFor(nameof(HasText), nameof(Excerpt), nameof(HasFiles), nameof(Files))]
    private SharedDraft? shared;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading), nameof(ShowNoBots), nameof(ShowBots), nameof(Choices))]
    private IReadOnlyList<BotSummary>? bots;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFailed), nameof(ShowLoading), nameof(ShowNoBots), nameof(ShowBots))]
    private string? problem;

    #endregion

    #region Properties

    public static string Title => "Share to Drover · Drover";

    public static string Heading => "Share to Drover";

    public static string NothingCause => "Nothing is waiting to be shared. Share text or a file to Drover from another app.";

    public static string GoToCrewLabel => "Go to your crew";

    public static string WhatYouSharedLabel => "What you shared";

    public static string FailedTitle => "Drover could not load your crew";

    public static string TryAgain => "Try again";

    public static string LoadingNote => "Loading your crew.";

    public static string NoBotsCause => "There is no bot to share this with yet. Make one first; what you shared waits here.";

    public static string Ask => "Which bot gets this?";

    public static string Note => "It opens as a draft in that bot's conversation. Nothing is sent until you send it.";

    public static string DiscardLabel => "Discard what you shared";

    public bool ShowNothing => Shared is null;

    public bool ShowShared => Shared is not null;

    public bool ShowFailed => Shared is not null && Problem is not null;

    public bool ShowLoading => Shared is not null && Problem is null && Bots is null;

    public bool ShowNoBots => Shared is not null && Problem is null && Bots is { Count: 0 };

    public bool ShowBots => Shared is not null && Problem is null && Bots is { Count: > 0 };

    public bool HasText => Shared is { Text.Length: > 0 };

    // Long text is shown from its start: enough to know it is the right thing. The whole of it goes into the draft.
    public string Excerpt => Shared?.Text is not { } text ? string.Empty
        : text.Length > ExcerptLength ? $"{text[..ExcerptLength]}…" : text;

    public bool HasFiles => Shared is { Files.Count: > 0 };

    /// <summary>Each shared file as its name and its size in words.</summary>
    public IReadOnlyList<(string Name, string Size)> Files => [.. (Shared?.Files ?? []).Select(file => (file.Name, Words.Size(file.Size)))];

    public IReadOnlyList<ShareBotChoice> Choices => [.. (Bots ?? []).Select(bot => new ShareBotChoice(bot, Choose))];

    #endregion

    #region Public Methods

    public async Task Open()
    {
        var signedIn = await session.Load() is not null;
        Shared = signedIn ? inbox.Waiting : null;

        if (Shared is not null)
        {
            await Load();
        }
    }

    [RelayCommand]
    public async Task Load()
    {
        Problem = null;

        try
        {
            Bots = await api.ListBots(session.Space!.Id);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ShareToViewModel));
            Problem = Failures.Sentence(exception);
        }
    }

    [RelayCommand]
    public void Discard()
    {
        inbox.Discard();
        navigator.NavigateTo(Addresses.Home);
    }

    #endregion

    #region Private Methods

    private void Choose(BotSummary bot)
    {
        inbox.SendTo(bot.Id);
        navigator.NavigateTo(Addresses.Talk(bot.Id));
    }

    #endregion
}
