namespace Drover.Ui.Session;

/// <summary>
/// Who is signed in, asked of the server once and kept for the life of the app.
/// </summary>
public sealed class SessionState(DroverApi api)
{
    #region Private Fields

    private bool loaded;

    #endregion

    #region Properties

    public SessionResponse? Current { get; private set; }

    /// <summary>The space the screens work in. A person's own space comes first.</summary>
    public SpaceSummary? Space => Current?.Spaces is [var first, ..] spaces
        ? spaces.FirstOrDefault(space => space.Kind == "personal") ?? first
        : null;

    public event Action? Changed;

    #endregion

    #region Public Methods

    public async Task<SessionResponse?> Load(CancellationToken cancellationToken = default)
    {
        if (!loaded)
        {
            Current = await api.GetSession(cancellationToken);
            loaded = true;
        }

        return Current;
    }

    /// <summary>Where a person with no session belongs: first-run setup while it is open, sign-in after.</summary>
    public async Task<string> FindEntryAddress(CancellationToken cancellationToken = default) =>
        await api.GetSetupStatus(cancellationToken) is null ? Addresses.SignIn : Addresses.Setup;

    public void Begin(SessionResponse session)
    {
        Current = session;
        loaded = true;
        Changed?.Invoke();
    }

    public async Task End(CancellationToken cancellationToken = default)
    {
        await api.SignOut(cancellationToken);
        Current = null;
        Changed?.Invoke();
    }

    #endregion
}
