namespace Drover.Ui.Bots;

/// <summary>
/// A bot as its editor holds it while a person types: every editable part, with numbers kept as the text in the
/// field so that a half-typed one is not lost. It becomes a request when it is saved.
/// </summary>
public sealed class BotDraft
{
    #region Private Fields

    private string handle = string.Empty;

    #endregion

    #region Properties

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The bot's handle. Setting it is a choice: it is how a person types one, so the handle stops following the name.
    /// The editor sets the handle a name makes through <see cref="Rename"/>, which is not a choice.
    /// </summary>
    public string Handle
    {
        get => handle;
        set
        {
            handle = value;
            HandleChosen = true;
        }
    }

    /// <summary>
    /// True when a person set the handle, here or in an earlier session (the server stores it), so a rename never changes
    /// it, whatever it is spelled like. False for a handle the name made, and for a bot from before this was stored,
    /// where the spelling is all there is to go on.
    /// </summary>
    public bool HandleChosen { get; set; }

    public BotAvatarBody Avatar { get; set; } = new(null, null);

    public string DescriptionForBots { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;

    public Guid ModelConnectionId { get; set; }

    public string Model { get; set; } = string.Empty;

    public string Temperature { get; set; } = string.Empty;

    public string MaxOutputTokens { get; set; } = string.Empty;

    /// <summary>Empty leaves the effort to the model.</summary>
    public string ReasoningEffort { get; set; } = string.Empty;

    public string WebSearch { get; set; } = "off";

    public bool StructuredOutput { get; set; }

    public bool MemoryEnabled { get; set; }

    public string Compaction { get; set; } = "none";

    public string CompactAtTokens { get; set; } = string.Empty;

    public string KeepRecentMessages { get; set; } = string.Empty;

    /// <summary>The bot's limits on its notes. Not edited here; carried back unchanged so saving keeps them.</summary>
    public int? MaxNoteCharacters { get; set; }

    public int? MaxNotesCharacters { get; set; }

    public string SandboxPolicy { get; set; } = "none";

    public string Network { get; set; } = "off";

    /// <summary>One host name to a line.</summary>
    public string AllowedHosts { get; set; } = string.Empty;

    public string IdleTimeoutMinutes { get; set; } = string.Empty;

    /// <summary>Whether the sandbox has a screen the bot sees and drives.</summary>
    public bool Desktop { get; set; }

    /// <summary>The model that reads the screen for the bot, or null for the bot's own model.</summary>
    public ScreenReaderBody? ScreenReader { get; set; }

    public IReadOnlyList<Guid> SandboxSharedWith { get; set; } = [];

    public string MaxChildren { get; set; } = string.Empty;

    public string MaxDepthBelow { get; set; } = string.Empty;

    /// <summary>The bot's own limits on one piece of its work. Empty leaves the space's limit.</summary>
    public string MaxSteps { get; set; } = string.Empty;

    public string MaxTokens { get; set; } = string.Empty;

    public bool UnlimitedTokens { get; set; }

    public bool UncappedUsageAcknowledged { get; set; }

    /// <summary>The bot's limit on rule checks, kept as it was read: the editor does not change it yet.</summary>
    public int? MaxJudgedChecks { get; set; }

    public string MaxDelegations { get; set; } = string.Empty;

    /// <summary>
    /// Tool grants a person chose in the Sandbox section, by tool, applied through the tools API when the bot is saved
    /// and dropped when the changes are discarded.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> StagedGrants { get; set; } = new Dictionary<Guid, string>();

    #endregion

    #region Public Methods

    public static BotDraft From(BotDetail bot) => new()
    {
        Name = bot.Name,
        Handle = bot.Handle,
        HandleChosen = bot.HandleChosen,
        Avatar = bot.Avatar,
        DescriptionForBots = bot.DescriptionForBots,
        Instructions = bot.Instructions,
        ModelConnectionId = bot.ModelConnectionId,
        Model = bot.Model,
        Temperature = Text(bot.ModelSettings.Temperature),
        MaxOutputTokens = Text(bot.ModelSettings.MaxOutputTokens),
        ReasoningEffort = bot.ModelSettings.ReasoningEffort ?? string.Empty,
        WebSearch = bot.ModelSettings.WebSearch,
        StructuredOutput = bot.ModelSettings.StructuredOutput,
        MemoryEnabled = bot.MemorySettings.Enabled,
        Compaction = bot.MemorySettings.Compaction,
        CompactAtTokens = Text(bot.MemorySettings.CompactAtTokens),
        KeepRecentMessages = Text(bot.MemorySettings.KeepRecentMessages),
        MaxNoteCharacters = bot.MemorySettings.MaxNoteCharacters,
        MaxNotesCharacters = bot.MemorySettings.MaxNotesCharacters,
        SandboxPolicy = bot.SandboxPolicy,
        Network = bot.SandboxSettings.Network,
        AllowedHosts = string.Join('\n', bot.SandboxSettings.AllowedHosts),
        IdleTimeoutMinutes = Text(bot.SandboxSettings.IdleTimeoutMinutes),
        Desktop = bot.SandboxSettings.Desktop,
        ScreenReader = bot.SandboxSettings.ScreenReader,
        SandboxSharedWith = bot.SandboxSharedWith,
        MaxChildren = Text(bot.BuildLimits.MaxChildren),
        MaxDepthBelow = Text(bot.BuildLimits.MaxDepthBelow),
        MaxSteps = Text(bot.RunLimits.MaxSteps),
        MaxTokens = Text(bot.RunLimits.MaxTokens),
        UnlimitedTokens = bot.RunLimits.UncappedUsageAcknowledged,
        UncappedUsageAcknowledged = bot.RunLimits.UncappedUsageAcknowledged,
        MaxDelegations = Text(bot.RunLimits.MaxDelegations),
        MaxJudgedChecks = bot.RunLimits.MaxJudgedChecks,
    };

    /// <summary>
    /// Sets the name. While the handle is still one the old name made, with or without the number the server adds to
    /// keep it unique, it follows the new name, so a bot that is renamed before anyone has chosen its handle is not
    /// left answering to the old name. A handle a person typed never follows. The server keeps a followed handle unique.
    /// </summary>
    public void Rename(string name)
    {
        if (!HandleChosen && BotHandles.Follows(Handle, Name) && BotHandles.Slug(name).Length > 0)
        {
            handle = BotHandles.Stem(name);
        }

        Name = name;
    }

    /// <summary>What a number field holds that is not a number, as a sentence, or null when every one reads.</summary>
    public string? FindProblem()
    {
        if (UnlimitedTokens && !UncappedUsageAcknowledged)
        {
            return "Confirm that you understand the API spending risk before saving.";
        }

        if (Temperature.Trim().Length > 0 && ReadDecimal(Temperature) is null)
        {
            return "Temperature is a number, such as 0.7.";
        }

        (string Name, string Value)[] wholeNumbers =
        [
            ("The longest answer", MaxOutputTokens),
            ("The size at which a conversation is shortened", CompactAtTokens),
            ("The number of recent messages to keep", KeepRecentMessages),
            ("The idle time", IdleTimeoutMinutes),
            ("The number of bots it may build", MaxChildren),
            ("The number of levels beneath it", MaxDepthBelow),
            ("The limit on model calls", MaxSteps),
            ("The limit on tokens", UnlimitedTokens ? string.Empty : MaxTokens),
            ("The limit on handoffs", MaxDelegations),
        ];

        foreach (var (name, value) in wholeNumbers)
        {
            if (value.Trim().Length > 0 && ReadWhole(value) is null)
            {
                return $"{name} is a whole number.";
            }
        }

        return null;
    }

    /// <summary>
    /// The request that saves this draft. It says whether the handle is the one the name makes (it was not chosen) or one
    /// a person set, so every editor that builds its save here, the native one and the web one, gives the server the same
    /// intent: it numbers a taken handle that follows the name, and records a chosen one so a rename never rewrites it.
    /// </summary>
    public UpdateBotRequest ToRequest() =>
        new(
            Name,
            Handle.Trim(),
            Avatar,
            DescriptionForBots,
            Instructions,
            ModelConnectionId,
            Model.Trim(),
            new ModelSettingsBody(ReadDecimal(Temperature), ReadWhole(MaxOutputTokens), ReasoningEffort.Length == 0 ? null : ReasoningEffort, WebSearch, StructuredOutput),
            new MemorySettingsBody(MemoryEnabled, Compaction, ReadWhole(CompactAtTokens), ReadWhole(KeepRecentMessages), MaxNoteCharacters, MaxNotesCharacters),
            SandboxPolicy,
            new SandboxSettingsBody(Network, Hosts, ReadWhole(IdleTimeoutMinutes), Desktop, ScreenReader),
            [.. SandboxSharedWith.Order()],
            new BuildLimitsBody(ReadWhole(MaxChildren), ReadWhole(MaxDepthBelow)),
            new RunLimitsBody(ReadWhole(MaxSteps), UnlimitedTokens ? null : ReadWhole(MaxTokens), ReadWhole(MaxDelegations), UnlimitedTokens && UncappedUsageAcknowledged, MaxJudgedChecks),
            HandleFollowsName: !HandleChosen);

    /// <summary>
    /// Clears the model settings the chosen model does not have, so a setting made for another model is not sent
    /// to one that would refuse it. A model whose abilities are unknown keeps everything.
    /// </summary>
    public void KeepWhatModelSupports(ModelCapabilitiesResponse? known)
    {
        if (known is null)
        {
            return;
        }

        if (!known.ReasoningEfforts.Contains(ReasoningEffort, StringComparer.OrdinalIgnoreCase))
        {
            ReasoningEffort = string.Empty;
        }

        if (!known.WebSearch)
        {
            WebSearch = "off";
        }

        if (!known.StructuredOutput)
        {
            StructuredOutput = false;
        }
    }

    /// <summary>Whether this draft would save anything different from the other.</summary>
    public bool DiffersFrom(BotDraft other) =>
        !string.Equals(JsonSerializer.Serialize(ToRequest()), JsonSerializer.Serialize(other.ToRequest()), StringComparison.Ordinal)
        || !StagedGrants.OrderBy(pair => pair.Key).SequenceEqual(other.StagedGrants.OrderBy(pair => pair.Key));

    #endregion

    #region Private Methods

    private IReadOnlyList<string> Hosts =>
        [.. AllowedHosts.Split(['\n', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static string Text(double? number) => number?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Text(int? number) => number?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static double? ReadDecimal(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static int? ReadWhole(string text) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    #endregion
}
