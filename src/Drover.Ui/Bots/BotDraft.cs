namespace Drover.Ui.Bots;

/// <summary>
/// A bot as its editor holds it while a person types: every editable part, with numbers kept as the text in the
/// field so that a half-typed one is not lost. It becomes a request when it is saved.
/// </summary>
public sealed class BotDraft
{
    #region Properties

    public string Name { get; set; } = string.Empty;

    public string Handle { get; set; } = string.Empty;

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

    public string SandboxPolicy { get; set; } = "none";

    public string Network { get; set; } = "off";

    /// <summary>One host name to a line.</summary>
    public string AllowedHosts { get; set; } = string.Empty;

    public string IdleTimeoutMinutes { get; set; } = string.Empty;

    public IReadOnlyList<Guid> SandboxSharedWith { get; set; } = [];

    public string MaxChildren { get; set; } = string.Empty;

    public string MaxDepthBelow { get; set; } = string.Empty;

    #endregion

    #region Public Methods

    public static BotDraft From(BotDetail bot) => new()
    {
        Name = bot.Name,
        Handle = bot.Handle,
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
        SandboxPolicy = bot.SandboxPolicy,
        Network = bot.SandboxSettings.Network,
        AllowedHosts = string.Join('\n', bot.SandboxSettings.AllowedHosts),
        IdleTimeoutMinutes = Text(bot.SandboxSettings.IdleTimeoutMinutes),
        SandboxSharedWith = bot.SandboxSharedWith,
        MaxChildren = Text(bot.BuildLimits.MaxChildren),
        MaxDepthBelow = Text(bot.BuildLimits.MaxDepthBelow),
    };

    /// <summary>
    /// Sets the name. While the handle is still the one made from the old name, it follows the new one, so a bot
    /// that is renamed before anyone has chosen its handle is not left answering to the old name.
    /// </summary>
    public void Rename(string name)
    {
        if (Handle == Slug(Name) && Slug(name) is { Length: > 0 } follows)
        {
            Handle = follows;
        }

        Name = name;
    }

    /// <summary>A name as a handle: lower-case letters and digits, with a hyphen for each run of anything else.</summary>
    public static string Slug(string name) =>
        string.Join('-', new string([.. name.Trim().ToLowerInvariant().Select(letter => char.IsAsciiLetterOrDigit(letter) ? letter : ' ')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>What a number field holds that is not a number, as a sentence, or null when every one reads.</summary>
    public string? FindProblem()
    {
        if (Temperature.Trim().Length > 0 && ReadDecimal(Temperature) is null)
        {
            return "Temperature is a number, such as 0.7.";
        }

        (string Name, string Value)[] wholeNumbers =
        [
            ("The longest answer", MaxOutputTokens),
            ("The size at which a thread is shortened", CompactAtTokens),
            ("The number of recent messages to keep", KeepRecentMessages),
            ("The idle time", IdleTimeoutMinutes),
            ("The number of bots it may build", MaxChildren),
            ("The number of levels beneath it", MaxDepthBelow),
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
            new MemorySettingsBody(MemoryEnabled, Compaction, ReadWhole(CompactAtTokens), ReadWhole(KeepRecentMessages)),
            SandboxPolicy,
            new SandboxSettingsBody(Network, Hosts, ReadWhole(IdleTimeoutMinutes)),
            [.. SandboxSharedWith.Order()],
            new BuildLimitsBody(ReadWhole(MaxChildren), ReadWhole(MaxDepthBelow)));

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
        !string.Equals(JsonSerializer.Serialize(ToRequest()), JsonSerializer.Serialize(other.ToRequest()), StringComparison.Ordinal);

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
