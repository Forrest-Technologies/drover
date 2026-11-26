namespace Drover.Presentation.ViewModels;

/// <summary>Where the list of named providers stands.</summary>
public enum NamedProvidersState
{
    Loading,
    Loaded,
    Failed,
}

/// <summary>
/// The provider choice of a form that connects a model: Venice, the named providers the server offers in its order,
/// then another compatible server. The names, addresses and key rules all come from the server. What the form shows
/// for the chosen provider (the address behind "Change address", the key field, a link to where a key is made, the
/// line about reach) is decided here, so the two forms that connect a model say the same things.
/// </summary>
public sealed class NamedProviders(DroverApi api, ILogger logger)
{
    #region Private Fields

    public const string VeniceKind = "venice";

    public const string CompatibleKind = "openai_compatible";

    public const string FailedWords = "Could not read the provider list.";

    public const string RetryWords = "Try again";

    public const string LoadingWords = "Loading the provider list.";

    public const string ChangeAddressWords = "Change address";

    public const string ReachWords = "Drover's server must be able to reach this address.";

    private IReadOnlyList<ModelProviderPresetResponse> presets = [];

    #endregion

    #region Properties

    /// <summary>Raised when the list, the choice, or the address shown changes.</summary>
    public event Action? Changed;

    public NamedProvidersState State { get; private set; } = NamedProvidersState.Loading;

    /// <summary>The value of the chosen option: <see cref="VeniceKind"/>, <see cref="CompatibleKind"/>, or a named provider's id.</summary>
    public string Choice { get; private set; } = VeniceKind;

    /// <summary>True once a person asked to see the address of a named provider.</summary>
    public bool AddressRevealed { get; private set; }

    public IReadOnlyList<ModelProviderPresetResponse> Presets => presets;

    /// <summary>The kind the server stores for the choice. A named provider is a compatible server with an address.</summary>
    public string Kind => Choice == VeniceKind ? VeniceKind : CompatibleKind;

    public ModelProviderPresetResponse? Preset => presets.FirstOrDefault(preset => preset.Id == Choice);

    public IReadOnlyList<SegmentOption<string>> Options =>
    [
        new(VeniceKind, "Venice"),
        .. presets.Select(preset => new SegmentOption<string>(preset.Id, preset.DisplayName)),
        new(CompatibleKind, "Another compatible server"),
    ];

    public bool ShowsAddress => Kind == CompatibleKind && (Preset is null || AddressRevealed);

    public bool OffersAddressChange => Preset is not null && !AddressRevealed;

    public bool ShowsKey => Preset?.KeyRule != ModelProviderKeyRules.None;

    public string KeyLabel => Preset?.KeyRule == ModelProviderKeyRules.Optional ? "Key (optional)" : "Key";

    public string KeyHint => Kind switch
    {
        VeniceKind => "Made in your Venice account, under API keys.",
        _ when Preset is null => "Leave this empty if the server needs no key.",
        _ => "Stored encrypted. It is not shown again.",
    };

    /// <summary>The words of the link to where a key is made, drawn as plain text. Null where the provider gave no page.</summary>
    public string? KeyLinkText => Preset is { KeyPage: not null } preset ? $"Get a key from {preset.DisplayName}" : null;

    public string? KeyLinkAddress => Preset?.KeyPage;

    public string? ReachNote => Preset is { Local: true } ? ReachWords : null;

    public bool ShowsFailure => State == NamedProvidersState.Failed;

    public bool ShowsLoading => State == NamedProvidersState.Loading;

    #endregion

    #region Public Methods

    public async Task Load()
    {
        State = NamedProvidersState.Loading;
        Changed?.Invoke();

        try
        {
            presets = await api.ListModelProviderPresets();
            State = NamedProvidersState.Loaded;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "The list of named providers could not be read");
            presets = [];
            State = NamedProvidersState.Failed;
        }

        // A choice the new list no longer holds falls back to the one that always works.
        if (Choice != VeniceKind && Choice != CompatibleKind && Preset is null)
        {
            Choice = CompatibleKind;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Takes a choice and says what the address becomes: a named provider's own, empty when a person leaves one for
    /// another compatible server, and otherwise whatever it was.
    /// </summary>
    public string? Choose(string chosen)
    {
        var before = Preset;
        Choice = chosen;
        AddressRevealed = false;
        Changed?.Invoke();

        return Preset?.Address ?? (before is not null ? string.Empty : null);
    }

    public void RevealAddress()
    {
        AddressRevealed = true;
        Changed?.Invoke();
    }

    /// <summary>The name a new connection gets when a person gave none: the provider's, else the address's host.</summary>
    public string NameFor(string? address) => Preset?.DisplayName ?? (
        Uri.TryCreate(address, UriKind.Absolute, out var parsed) ? parsed.Host : "Venice");

    #endregion
}
