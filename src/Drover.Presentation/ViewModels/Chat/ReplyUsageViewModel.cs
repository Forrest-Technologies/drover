namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// What a finished reply used, as a quiet hint: the model that wrote it by a short name, and once read, the tokens and
/// the cost. The whole of it opens in a popover when the hint is pressed. The newest reply's hint stands on the line
/// under the composer and is read once when the reply finishes (<see cref="ConversationStatusViewModel"/>); an older
/// reply's stands in its footer and is read only when asked for, so a conversation with many replies makes no request
/// for them until a person opens one.
/// </summary>
public sealed partial class ReplyUsageViewModel(Guid runId, Func<Guid, Task<RunUsageDetail>> load, ILogger logger, string? model = null)
    : ObservableObject
{
    #region Private Fields

    private const int ShortLength = 24;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Lines), nameof(IsAdding))]
    private bool open;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Lines), nameof(IsAdding), nameof(Figures), nameof(HintName))]
    private RunUsageDetail? detail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Lines), nameof(IsAdding))]
    private string? problem;

    #endregion

    #region Properties

    public static string Label => "What this reply used";

    /// <summary>Said, politely, while the server adds it up.</summary>
    public static string Adding => "Adding it up.";

    public bool IsAdding => Open && Problem is null && Detail is null;

    /// <summary>The model that wrote the reply, in words, when the server kept it.</summary>
    public string? WrittenBy => model is null ? null : $"Written by {model}";

    /// <summary>The model's whole name, for the hint's tooltip, when the server kept it.</summary>
    public string? ModelName => model;

    /// <summary>The model by a short name that fits the footer, when the server kept it.</summary>
    public string? ShortModel => model is null ? null : ShortName(model);

    /// <summary>The tokens and the cost of the reply, short, once they are read: "1.2k tokens, USD 0.02".</summary>
    public string? Figures => Detail is { } read ? ShortFigures(read) : null;

    /// <summary>
    /// What the hint is called for someone who cannot see it: the model, the figures in words, and what pressing does.
    /// "Written by vendor/model, 1,200 tokens, USD 0.02. Show what this reply used."
    /// </summary>
    public string HintName => SpokenFigures() is { Length: > 0 } said ? $"{said}. {ShowLabel}." : $"{ShowLabel}.";

    /// <summary>What pressing the hint does.</summary>
    public static string ShowLabel => "Show what this reply used";

    /// <summary>The one or two lines that say what the reply used, once it is known.</summary>
    public IReadOnlyList<string> Lines => Open && Problem is null && Detail is { } read ? UsageWords.Reply(read) : [];

    #endregion

    #region Public Methods

    /// <summary>
    /// A model's name short enough for one line of the footer: the last part of a name that has a path, and at most
    /// <see cref="ShortLength"/> characters, with an ellipsis where it was cut.
    /// </summary>
    public static string ShortName(string model)
    {
        var name = model.Trim().TrimEnd('/');
        var last = name[(name.LastIndexOf('/') + 1)..];
        return last.Length <= ShortLength ? last : $"{last[..(ShortLength - 1)].TrimEnd('-', '_', '.', ' ')}…";
    }

    /// <summary>A count of tokens in a few characters: 950, 1.2k, 34k, 1.5M.</summary>
    public static string ShortTokens(long tokens) => tokens switch
    {
        < 1_000 => tokens.ToString(CultureInfo.InvariantCulture),
        < 10_000 => $"{(tokens / 1_000d).ToString("0.#", CultureInfo.InvariantCulture)}k",
        < 1_000_000 => $"{(tokens / 1_000).ToString(CultureInfo.InvariantCulture)}k",
        _ => $"{(tokens / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture)}M",
    };

    [RelayCommand]
    public async Task Toggle()
    {
        Open = !Open;

        if (Open && Detail is null)
        {
            await Read();
        }
    }

    [RelayCommand]
    public async Task Read()
    {
        Problem = null;

        try
        {
            Detail = await load(runId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "Could not read what run {RunId} used", runId);
            Problem = Failures.Sentence(exception);
        }
    }

    #endregion

    #region Private Methods

    // The reply with the bots it asked when there were any, since that is what it cost. A price the server does not
    // know is left out, never shown as nothing.
    private static string ShortFigures(RunUsageDetail read)
    {
        var figure = read.Tree ?? read.Own;
        var tokens = $"{ShortTokens(figure.InputTokens + figure.OutputTokens)} tokens";
        return figure.Cost is null ? tokens : $"{tokens}, {UsageWords.Cost(figure, read.Currency)}";
    }

    // The model and the figures as they are read out: the whole name, and every token counted.
    private string SpokenFigures()
    {
        List<string> parts = [];

        if (WrittenBy is { } writtenBy)
        {
            parts.Add(writtenBy);
        }

        if (Detail is { } read)
        {
            var figure = read.Tree ?? read.Own;
            parts.Add($"{UsageWords.Tokens(figure.InputTokens + figure.OutputTokens)} tokens");

            if (figure.Cost is not null)
            {
                parts.Add(UsageWords.Cost(figure, read.Currency));
            }
        }

        return string.Join(", ", parts);
    }

    #endregion
}
