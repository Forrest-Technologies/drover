namespace Drover.Presentation.ViewModels;

/// <summary>One bot a share can go to: its name and its look.</summary>
public sealed record ShareBotChoice(BotSummary Bot)
{
    #region Properties

    public string Name => Bot.Name;

    public int Hue => BotIdentity.Hue(Bot.Id);

    #endregion
}

/// <summary>This screen's job: choose which bot's conversation gets what another app shared, as a draft.</summary>
public sealed partial class ShareToViewModel(
    DroverApi api, SessionState session, ShareTray tray, Navigator navigator, ILogger<ShareToViewModel> logger) : ObservableObject
{
    #region Private Fields

    private const int ExcerptLength = 400;

    private static readonly string[] stateNames =
    [
        nameof(ShowNothing), nameof(ShowShared), nameof(ShowFailed), nameof(ShowLoading), nameof(ShowNoBots), nameof(ShowBots),
        nameof(Excerpt), nameof(HasText), nameof(Files), nameof(HasFiles), nameof(Choices),
    ];

    private SharedContent? shared;

    private IReadOnlyList<BotSummary>? bots;

    [ObservableProperty]
    private string? problem;

    #endregion

    #region Properties

    public static string Title => "Share to Drover · Drover";

    public static string Heading => "Share to Drover";

    public static string NothingCause => "Nothing is waiting to be shared. Share text or a file to Drover from another app.";

    public static string GoToCrewLabel => "Go to your crew";

    public static string WhatYouSharedLabel => "What you shared";

    public static string FailedTitle => "Drover could not load your crew";

    public static string LoadingNote => "Loading your crew.";

    public static string NoBotsCause => "There is no bot to share this with yet. Make one first; what you shared waits here.";

    public static string Ask => "Which bot gets this?";

    public static string Note => "It opens as a draft in that bot's conversation. Nothing is sent until you send it.";

    public static string DiscardLabel => "Discard what you shared";

    public bool ShowNothing => shared is null;

    public bool ShowShared => shared is not null;

    public bool ShowFailed => shared is not null && Problem is not null;

    public bool ShowLoading => shared is not null && Problem is null && bots is null;

    public bool ShowNoBots => shared is not null && Problem is null && bots is { Count: 0 };

    public bool ShowBots => shared is not null && Problem is null && bots is { Count: > 0 };

    public bool HasText => shared is { Text.Length: > 0 };

    // Long text is shown from its start: enough to know it is the right thing. The whole of it goes into the draft.
    public string Excerpt => shared is null ? string.Empty : shared.Text.Length > ExcerptLength ? $"{shared.Text[..ExcerptLength]}…" : shared.Text;

    public bool HasFiles => shared is { Files.Count: > 0 };

    /// <summary>Each file as one line: its name and its size.</summary>
    public IReadOnlyList<ShareFile> Files => shared?.Files ?? [];

    public IReadOnlyList<ShareBotChoice> Choices => [.. (bots ?? []).Select(bot => new ShareBotChoice(bot))];

    #endregion

    #region Public Methods

    public async Task Start()
    {
        shared = tray.Waiting;
        Show();

        if (shared is not null && await session.Load() is not null)
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
            bots = await api.ListBots(session.Space!.Id);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ShareToViewModel));
            Problem = Failures.Sentence(exception);
        }

        Show();
    }

    public void Choose(ShareBotChoice choice)
    {
        tray.SendTo(choice.Bot.Id);
        navigator.NavigateTo(Addresses.Talk(choice.Bot.Id));
    }

    [RelayCommand]
    public void Discard()
    {
        tray.Discard();
        navigator.NavigateTo(Addresses.Home);
    }

    #endregion

    #region Private Methods

    private void Show()
    {
        foreach (var stateName in stateNames)
        {
            OnPropertyChanged(stateName);
        }
    }

    #endregion
}
