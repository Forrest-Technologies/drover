namespace Drover.Presentation.ViewModels;

/// <summary>
/// The form that adds a tool server. Secrets typed here are sent once and never shown again. A form is made when
/// adding starts and dropped when it ends, so nothing typed into it outlives it.
/// </summary>
public sealed partial class AddConnectionFormViewModel(Func<ConnectionDraft, Task> add, Action cancel) : ObservableObject
{
    #region Private Fields

    private static readonly SegmentOption<string>[] kinds = [new("none", "Nothing"), new("header", "A header"), new("oauth", "A sign-in")];

    private static readonly Choice[] signInScopes =
    [
        new("per_user", "Each person signs in as themselves"),
        new("space", "One sign-in for the whole space"),
    ];

    private static readonly Choice[] protocolModes =
    [
        new("automatic", "Chosen by Drover"),
        new("stateless_only", "Without sessions only"),
        new("session_only", "With sessions only"),
    ];

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string url = string.Empty;

    /// <summary>"none", "header", or "oauth".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHeader), nameof(ShowSignIn))]
    private string authKind = "none";

    [ObservableProperty]
    private string headerName = "Authorization";

    [ObservableProperty]
    private string headerValue = string.Empty;

    /// <summary>"per_user" or "space".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScopeMeaning))]
    private string signInScope = "per_user";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOwnClient), nameof(ShowNoOwnClient))]
    private bool ownClient;

    [ObservableProperty]
    private string clientId = string.Empty;

    [ObservableProperty]
    private string clientSecret = string.Empty;

    [ObservableProperty]
    private string issuer = string.Empty;

    [ObservableProperty]
    private bool trusted;

    [ObservableProperty]
    private string protocolMode = "automatic";

    /// <summary>What the server said was wrong with the last attempt.</summary>
    [ObservableProperty]
    private string? problem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubmitLabel), nameof(Enabled))]
    private bool busy;

    #endregion

    #region Properties

    public static string Heading => "Add a tool server";

    public static string ProofLabel => "How Drover proves who is calling";

    public static IReadOnlyList<SegmentOption<string>> Kinds => kinds;

    public static IReadOnlyList<Choice> SignInScopes => signInScopes;

    public static IReadOnlyList<Choice> ProtocolModes => protocolModes;

    public bool ShowHeader => AuthKind is "header";

    public bool ShowSignIn => AuthKind is "oauth";

    public bool ShowOwnClient => ShowSignIn && OwnClient;

    public bool ShowNoOwnClient => ShowSignIn && !OwnClient;

    public bool Enabled => !Busy;

    public string SubmitLabel => Busy ? "Adding the server" : "Add server";

    public string ScopeMeaning => SignInScope is "space"
        ? "Every bot run in the space acts as the one account an owner or admin signs in with."
        : "A bot run acts as the person it runs for, and can use this server once that person has signed in.";

    #endregion

    #region Public Methods

    public ConnectionDraft ToDraft() => new()
    {
        Name = Name,
        Url = Url,
        AuthKind = AuthKind,
        HeaderName = HeaderName,
        HeaderValue = HeaderValue,
        SignInScope = SignInScope,
        OwnClient = OwnClient,
        ClientId = ClientId,
        ClientSecret = ClientSecret,
        Issuer = Issuer,
        Trusted = Trusted,
        ProtocolMode = ProtocolMode,
    };

    [RelayCommand]
    public Task Add() => add(ToDraft());

    [RelayCommand]
    public void Cancel() => cancel();

    #endregion
}
