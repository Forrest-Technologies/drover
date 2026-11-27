namespace Drover.Presentation.ViewModels;

/// <summary>Where the list of kinds stands.</summary>
public enum NamedProvidersState
{
    Loading,
    Loaded,
    Failed,
}

/// <summary>
/// The provider choice of a form that connects a model, drawn from the kinds the server offers, in its order. A kind
/// that carries named providers contributes them first, then itself. What the form shows for the choice (the address
/// behind "Change address", the key field and its label, the hints, a link to where a key is made, the line about
/// reach) is decided here from the server's descriptors, so the two forms that connect a model say the same things
/// and a kind the server gains needs no client release. The one kind the client names is the compatible one, because
/// it is the kind a named provider is a starting point for.
/// </summary>
public sealed class NamedProviders(DroverApi api, ILogger logger)
{
    #region Private Fields

    public const string CompatibleKind = "openai_compatible";

    public const string FailedTitle = "Drover could not load the providers";

    public const string NoneTitle = "This server offers no model provider";

    public const string NoneCause = "The operator of this server has switched every provider off. Ask them to turn one on, then try again.";

    public const string RetryWords = "Try again";

    /// <summary>What the failure note says before the server has given a sentence.</summary>
    public const string FailedCause = "Check that the server is running.";

    public const string LoadingWords = "Asking this server which providers it offers.";

    public const string ChangeAddressWords = "Change address";

    public const string ReachWords = "Drover's server must be able to reach this address.";

    public const string AddressMissingWords = "Enter the base address.";

    public const string KeyMissingWords = "Enter the key.";

    /// <summary>The largest number of choices shown as a segmented control. Five or more are a list.</summary>
    public const int MostSegments = 4;

    private const string AnotherCompatibleWords = "Another compatible server";

    private IReadOnlyList<ModelProviderKindResponse> kinds = [];

    private string blankValue = string.Empty;

    #endregion

    #region Properties

    /// <summary>Raised when the list, the choice, or the address shown changes.</summary>
    public event Action? Changed;

    public NamedProvidersState State { get; private set; } = NamedProvidersState.Loading;

    /// <summary>
    /// The value of the chosen option: a kind, or a named provider's id. Empty until the list has been read, and then
    /// the server's first option.
    /// </summary>
    public string Choice { get; private set; } = string.Empty;

    /// <summary>True once a person asked to see the address of a named provider.</summary>
    public bool AddressRevealed { get; private set; }

    public IReadOnlyList<ModelProviderKindResponse> Kinds => kinds;

    public IReadOnlyList<ModelProviderPresetResponse> Presets => [.. kinds.SelectMany(kind => kind.NamedProviders)];

    public ModelProviderPresetResponse? Selected => Presets.FirstOrDefault(preset => preset.Id == Choice);

    /// <summary>What the server says about the kind of the choice. Null until the list is read, and when it cannot be.</summary>
    public ModelProviderKindResponse? Descriptor =>
        kinds.FirstOrDefault(kind => kind.NamedProviders.Any(preset => preset.Id == Choice)) ??
        kinds.FirstOrDefault(kind => kind.Kind == Choice);

    /// <summary>The chosen kind when each person connects it by signing in with their own account. It has no form.</summary>
    public ModelProviderKindResponse? SignIn => Descriptor is { SignIn: true } kind && kind.Kind == Choice ? kind : null;

    /// <summary>Where "Add a key" goes from a sign-in: the first kind the server offers that takes a key.</summary>
    public string KeyChoice => kinds.FirstOrDefault(kind => !kind.SignIn && kind.KeyRule != ModelKeyRules.None)?.Kind ?? CompatibleKind;

    /// <summary>The kind the server stores for the choice. A named provider is a compatible server with an address.</summary>
    public string Kind => Descriptor?.Kind ?? Choice;

    public IReadOnlyList<SegmentOption<string>> Options => [.. kinds.SelectMany(OptionsOf)];

    /// <summary>Whether the form asks for a provider at all. A server with one kind and nothing named shows no choice.</summary>
    public bool ShowsChoice => Options.Count > 1;

    /// <summary>Whether the choice is drawn as a list rather than as segments. The order is the server's either way.</summary>
    public bool UsesList => Options.Count > MostSegments;

    /// <summary>The line that says which kind a server with one kind uses, or null.</summary>
    public string? OnlyKindWords => State == NamedProvidersState.Loaded && !ShowsChoice && Descriptor is { } only
        ? $"This server uses {only.DisplayName}."
        : null;

    /// <summary>Whether a connection of the choice carries an address. A kind the list does not hold is asked for one.</summary>
    public bool TakesAddress => Kind.Length > 0 && (Descriptor?.AddressRule ?? ModelAddressRules.Required) == ModelAddressRules.Required;

    /// <summary>The one value the chosen provider's address needs from the person, when it needs one.</summary>
    public ModelProviderAddressBlankResponse? Blank => Selected?.AddressBlank;

    /// <summary>What the person typed for <see cref="Blank"/>.</summary>
    public string BlankValue => blankValue;

    /// <summary>The address built from the person's value, or null when the chosen provider has no blank or the value is not allowed.</summary>
    public string? BlankAddress => Selected is { AddressBlank: { } blank } preset ? AddressBlanks.Build(preset.Address, blank.Pattern, blankValue.Trim()) : null;

    /// <summary>What is wrong with the value in the blank, or null when it is fine (or there is no blank).</summary>
    public string? BlankProblem => Blank is { } blank && BlankAddress is null ? $"{blank.Label} is not valid." : null;

    /// <summary>The chosen provider's blank replaces the address field: the address is never typed whole.</summary>
    public bool ShowsBlank => Blank is not null;

    public bool ShowsAddress => Blank is null && (Selected is null ? TakesAddress : AddressRevealed);

    public bool OffersAddressChange => Selected is not null && Blank is null && !AddressRevealed;

    public string? AddressHint => Selected is null ? Descriptor?.AddressHint : null;

    public bool ShowsKey => SignIn is null && (Selected is { } named
        ? named.KeyRule != ModelProviderKeyRules.None
        : Kind.Length > 0 && (Descriptor?.KeyRule ?? ModelKeyRules.Optional) != ModelKeyRules.None);

    public bool KeyRequired => Selected is { } named
        ? named.KeyRule == ModelProviderKeyRules.Required
        : Descriptor?.KeyRule == ModelKeyRules.Required;

    public string KeyLabel => ShowsKey && !KeyRequired ? "Key (optional)" : "Key";

    /// <summary>The server's words under the key field. Plain text, never a link.</summary>
    public string KeyHint => Selected is null ? Descriptor?.KeyHint ?? string.Empty : string.Empty;

    /// <summary>The words of the link to where a key is made, drawn as plain text. Null where the provider gave no page.</summary>
    public string? KeyLinkText => KeyLinkAddress is null ? null : $"Get a key from {Selected!.DisplayName}";

    /// <summary>The page where a key is made, only when it is an absolute https address.</summary>
    public string? KeyLinkAddress =>
        Selected?.KeyPage is { } page && Uri.TryCreate(page, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps
            ? page
            : null;

    public string? ReachNote => Selected is { Local: true } ? ReachWords : null;

    public bool ShowsFailure => State == NamedProvidersState.Failed;

    /// <summary>The server's sentence for why the kinds could not be read, shown under the failure title.</summary>
    public string? FailureWords { get; private set; }

    /// <summary>True when the kinds were read and the server offers none, for example when the operator switched them all off.</summary>
    public bool OffersNone => State == NamedProvidersState.Loaded && kinds.Count == 0;

    /// <summary>Whether the form is drawn. It is not while the kinds are being read, when they could not be, or when there are none.</summary>
    public bool ShowsForm => State == NamedProvidersState.Loaded && kinds.Count > 0;

    /// <summary>Whether a state is drawn in place of the form: the kinds could not be read, or there are none.</summary>
    public bool ShowsState => ShowsFailure || OffersNone;

    public string StateTitle => OffersNone ? NoneTitle : FailedTitle;

    public string StateCause => OffersNone ? NoneCause : FailureWords ?? FailedCause;

    public bool ShowsLoading => State == NamedProvidersState.Loading;

    #endregion

    #region Public Methods

    public async Task Load()
    {
        State = NamedProvidersState.Loading;
        FailureWords = null;
        Changed?.Invoke();

        try
        {
            kinds = await api.ListModelProviderKinds();
            State = NamedProvidersState.Loaded;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "The list of provider kinds could not be read");
            kinds = [];
            FailureWords = Failures.Sentence(exception, "Check that the server is running.");
            State = NamedProvidersState.Failed;
        }

        KeepChoiceOffered();
        Changed?.Invoke();
    }

    /// <summary>
    /// Takes a choice and says what the address becomes: a named provider's own, empty when a person leaves one for
    /// another kind, and otherwise whatever it was.
    /// </summary>
    public string? Choose(string chosen)
    {
        var before = Selected;
        Choice = chosen;
        AddressRevealed = false;
        blankValue = string.Empty;
        Changed?.Invoke();

        // A provider with a blank has no address until the person fills it in.
        return Selected is null ? (before is not null ? string.Empty : null) : Selected.AddressBlank is null ? Selected.Address : string.Empty;
    }

    public void SetBlank(string value)
    {
        blankValue = value;
        Changed?.Invoke();
    }

    public void RevealAddress()
    {
        AddressRevealed = true;
        Changed?.Invoke();
    }

    /// <summary>The sign-in kind the server offers with this name, or null.</summary>
    public ModelProviderKindResponse? SignInKindOf(string kind) => kinds.FirstOrDefault(offered => offered.SignIn && offered.Kind == kind);

    /// <summary>Whether this server offers the kind. False while the list is unread or unreadable.</summary>
    public bool Offers(string kind) => kinds.Any(offered => offered.Kind == kind);

    /// <summary>The name a new connection gets when a person gave none: the provider's, else the address's host, else the kind's.</summary>
    public string NameFor(string? address) => Selected?.DisplayName ?? (
        Uri.TryCreate(address, UriKind.Absolute, out var parsed) ? parsed.Host : Descriptor?.DisplayName ?? Kind);

    /// <summary>What is missing from the form for a request, as the words under the field, or null for a field that is filled.</summary>
    public (string? Address, string? Key) Missing(string address, string key) =>
        (ShowsBlank ? BlankProblem : ShowsAddress && address.Trim().Length == 0 ? AddressMissingWords : null,
         ShowsKey && KeyRequired && key.Trim().Length == 0 ? KeyMissingWords : null);

    #endregion

    #region Private Methods

    private static IEnumerable<SegmentOption<string>> OptionsOf(ModelProviderKindResponse kind) =>
    [
        .. kind.NamedProviders.Select(preset => new SegmentOption<string>(preset.Id, preset.DisplayName)),
        new(kind.Kind, kind.Kind == CompatibleKind ? AnotherCompatibleWords : kind.DisplayName),
    ];

    // The first option is chosen at first. A choice the new list no longer holds falls back to the first option.
    private void KeepChoiceOffered()
    {
        var offered = Options;
        if (offered.All(option => option.Value != Choice))
        {
            Choice = offered.Count > 0 ? offered[0].Value : string.Empty;
        }
    }

    #endregion
}
