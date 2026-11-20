namespace Drover.Presentation.Diagnostics;

/// <summary>
/// The diagnostic log kept on this device, so a person can attach it to a bug report. Nothing in it is sent anywhere.
/// It is written in two parts of at most <see cref="PartLimit"/> characters each: when the newer part is full, it
/// replaces the older one and a new part starts, so the log never holds more than twice that. The last failure that
/// nothing handled is kept apart, so it outlives the parts, and the next start of the app offers the log once.
/// Every entry is made by <see cref="DiagnosticEntry"/>, which keeps only what is known to be safe.
/// </summary>
public sealed class DiagnosticLog(IDiagnosticStore store, TimeProvider time)
{
    #region Private Fields

    private const string Newer = "diagnostics";

    private const string Older = "diagnostics-older";

    private const string LastFailure = "diagnostics-last-failure";

    private const string FailureNotOffered = "diagnostics-failure-not-offered";

    private const string Cut = "\n(cut to fit the log)\n";

    private readonly Lock gate = new();

    private string? problem;

    #endregion

    #region Properties

    /// <summary>The most characters one part of the log holds. The log is two parts.</summary>
    public const int PartLimit = 64 * 1024;

    #endregion

    #region Public Methods

    public void Write(string entry)
    {
        lock (gate)
        {
            Guarded(() => Append(Fit(entry)));
        }
    }

    /// <summary>
    /// A failure nothing handled, which may be the last thing the app does. Written at once, apart from the log, and
    /// offered at the next start.
    /// </summary>
    public void RecordFailure(Exception exception)
    {
        var failure = Fit(DiagnosticEntry.Failure(time.GetUtcNow(), exception));
        lock (gate)
        {
            Guarded(() =>
            {
                store.Write(LastFailure, failure);
                store.Write(FailureNotOffered, Stamp());
            });
        }
    }

    /// <summary>
    /// Records an unhandled failure in storage and logs a fixed event without passing <paramref name="exception"/>,
    /// so console and platform sinks never write exception messages or stack paths.
    /// </summary>
    public void ReportUnhandled(ILogger logger, LogLevel level, EventId eventId, Exception exception)
    {
        RecordFailure(exception);
        var failureType = exception.GetType().FullName ?? exception.GetType().Name;
        logger.Log(level, eventId, "Unhandled failure {FailureType} (result {HResult})", failureType, exception.HResult);
    }

    /// <summary>True once after a failure nothing handled: the app offers the log then, and not again for it.</summary>
    public bool TakeFailureOffer()
    {
        lock (gate)
        {
            var waiting = false;
            Guarded(() =>
            {
                waiting = store.Read(FailureNotOffered) is not null;
                if (waiting)
                {
                    store.Remove(FailureNotOffered);
                }
            });
            return waiting;
        }
    }

    /// <summary>The file a person saves: what the app is, the last failure, then the log, oldest first.</summary>
    public string Compose(AppDescription app)
    {
        string? older = null, newer = null, failure = null;
        lock (gate)
        {
            Guarded(() =>
            {
                older = Tail(store.Read(Older));
                newer = Tail(store.Read(Newer));
                failure = Tail(store.Read(LastFailure));
            });
        }

        var logged = $"{older}{newer}";
        return $"""
            Drover diagnostic log
            App version: {app.Version}
            Platform: {app.Platform}
            Saved: {Stamp()}

            This file holds the warnings and failures of the app on this device: what failed and where in the app's
            code. Message text, names, addresses, ids, passwords and tokens are never written to it.

            Last failure nothing handled
            {(failure is { Length: > 0 } ? failure : "None recorded.\n")}
            Log, oldest first
            {(logged.Length > 0 ? logged : "Nothing logged.\n")}{Problem()}
            """;
    }

    #endregion

    #region Private Methods

    private void Append(string entry) => store.Append(Newer, Older, entry, PartLimit);

    private static string? Tail(string? text) =>
        text is { Length: > PartLimit } ? text[^PartLimit..] : text;

    private string Problem() =>
        problem is null ? string.Empty : $"\nSome entries could not be kept on this device ({problem}).\n";

    private static string Fit(string entry) =>
        entry.Length <= PartLimit ? entry : $"{entry[..(PartLimit - Cut.Length)]}{Cut}";

    private string Stamp() => time.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    // The log is how a failure is reported, so a failure to write it has nowhere else to go: logging it would come
    // straight back here. The app carries on without the entry and the saved file says what kind of failure it was.
    private void Guarded(Action write) => problem = Attempt(write) ?? problem;

    private static string? Attempt(Action write)
    {
        try
        {
            write();
            return null;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return exception.GetType().FullName;
        }
    }

    #endregion
}
