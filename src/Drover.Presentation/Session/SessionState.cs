namespace Drover.Ui.Session;

/// <summary>
/// Who is signed in, asked of the server once and kept for the life of the app.
/// </summary>
public sealed class SessionState(DroverApi api)
{
    #region Private Fields

    private bool loaded;

    private long openGeneration;

    #endregion

    #region Properties

    public SessionResponse? Current { get; private set; }

    /// <summary>The space the screens work in. A person's own space comes first.</summary>
    public SpaceSummary? Space => Current?.Spaces is [var first, ..] spaces
        ? spaces.FirstOrDefault(space => space.Kind == "personal") ?? first
        : null;

    public event Action? Changed;

    /// <summary>
    /// True when the screen last opened could not reach the server. The screen's gate says so, with a way to try
    /// again.
    /// </summary>
    public bool Unreachable { get; private set; }

    /// <summary>Raised when a screen could not reach the server, or a screen is opened again.</summary>
    public event Action? ReachChanged;

    #endregion

    #region Public Methods

    public async Task<SessionResponse?> Load(CancellationToken cancellationToken = default)
    {
        if (!loaded)
        {
            Current = await api.GetSession(cancellationToken);
            loaded = true;
            Changed?.Invoke();
        }

        return Current;
    }

    /// <summary>Where a person with no session belongs: first-run setup while it is open, sign-in after.</summary>
    public async Task<string> FindEntryAddress(CancellationToken cancellationToken = default) =>
        await api.GetSetupStatus(cancellationToken) is null ? Addresses.SignIn : Addresses.Setup;

    /// <summary>
    /// Opens a screen. A request that fails on the way is kept in <see cref="Unreachable"/> for the screen's gate to
    /// show, instead of escaping into the platform, where nothing catches it and the app closes.
    /// </summary>
    public async Task OpenScreen(Func<Task> open, ILogger logger)
    {
        var generation = Interlocked.Increment(ref openGeneration);
        SetUnreachable(false);
        try
        {
            await open();
        }
        catch (Exception exception) when (Failures.IsUnreachable(exception))
        {
            logger.LogWarning(
                "A screen could not reach the server while it opened ({ExceptionType}, {StatusCode})",
                exception.GetType().Name,
                StatusCodeOf(exception));
            if (Volatile.Read(ref openGeneration) == generation)
            {
                SetUnreachable(true);
            }
        }
    }

    /// <summary>
    /// Opens a screen from the native shell's fire-and-forget navigation callback. Unreachable failures trigger the
    /// offline gate via <see cref="OpenScreen"/>; server refusals and unreadable answers that a screen did not catch
    /// itself are logged and stopped here so they cannot escape to the platform handler and close the app.
    /// </summary>
    public async Task OpenAtNavigationBoundary(Func<Task> open, ILogger logger)
    {
        try
        {
            await OpenScreen(open, logger);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(
                "A screen stopped opening because the server refused the request ({ExceptionType}, {StatusCode})",
                exception.GetType().Name,
                StatusCodeOf(exception));
        }
    }

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

    #region Private Methods

    private static int? StatusCodeOf(Exception exception) => exception switch
    {
        DroverApiException refusal => (int)refusal.StatusCode,
        HttpRequestException { StatusCode: { } status } => (int)status,
        _ => null,
    };

    private void SetUnreachable(bool unreachable)
    {
        Unreachable = unreachable;
        ReachChanged?.Invoke();
    }

    #endregion
}
