namespace Drover.Engine.Runs;

/// <summary>How the run engine paces itself. The defaults are the numbers in docs/architecture/03-run-lifecycle.md.</summary>
public sealed class RunOptions
{
    #region Properties

    /// <summary>How many runs one server executes at once.</summary>
    public int Workers { get; set; } = 8;

    /// <summary>How often the dispatcher looks for work when no signal arrives. A lost signal costs this long.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long the dispatcher waits to claim again after a claim failed. Stopping ends the wait.</summary>
    public TimeSpan ClaimRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How often a worker looks at its run for a cancel request or a lost lease.</summary>
    public TimeSpan WatchInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan LeaseRenewInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>The most characters of one tool result a model is given. A longer result is cut, and says so.</summary>
    public int MaxToolResultCharacters { get; set; } = 100_000;

    /// <summary>How long one call to a tool server may take.</summary>
    public TimeSpan ToolCallTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How long the model that reads a bot's screen may take to describe one screenshot.</summary>
    public TimeSpan ScreenReadTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>A run taken over this many times after a lost lease fails instead of starting again.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// The most characters of output one tool call streams into the run's events. Past it the rest is left out of the
    /// log, which says so once; the tool's result is not affected.
    /// </summary>
    public int MaxToolOutputCharacters { get; set; } = 200_000;

    /// <summary>
    /// How many person and bot messages must lie beyond a main thread's summary before the server writes a new one.
    /// Counted after the run that ends, newest included.
    /// </summary>
    public int SummaryAfterMessages { get; set; } = 20;

    /// <summary>
    /// How many of the newest person and bot messages a summary always leaves out, so it never reaches into the
    /// exchange still going on. They are summarized later, once newer messages have followed them.
    /// </summary>
    public int SummaryKeepsRecentMessages { get; set; } = 6;

    public TimeSpan DeltaFlushInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    public int DeltaFlushCharacters { get; set; } = 512;

    /// <summary>How long an event stream stays silent before it sends a heartbeat and looks at the table again.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long a push the push service could not take waits before each new try. After the last one it is dropped:
    /// a wake-up hours late is no longer worth waking a device for.
    /// </summary>
    public IReadOnlyList<TimeSpan> PushRetryDelays { get; set; } =
        [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];

    #endregion
}
