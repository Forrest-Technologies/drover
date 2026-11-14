namespace Drover.Engine.Sandboxes;

/// <summary>Why a person's action on a bot's sandbox was not done.</summary>
public enum SandboxControlRefusal
{
    None,

    /// <summary>The person is not in the space, or the bot is not there.</summary>
    NotFound,

    /// <summary>The person is in the space but may not change its bots.</summary>
    NotPermitted,

    /// <summary>The request itself is wrong: an unknown key, a point off the screen, an empty command.</summary>
    Invalid,

    /// <summary>The server, the deployment's rules, the bot's settings, or the provider refused; the words say which.</summary>
    Refused,
}


/// <summary>The outcome of a person's action on a bot's sandbox: the value, or why there is none, in plain words.</summary>
public sealed record SandboxControlResult<T>(T? Value, SandboxControlRefusal Refusal = SandboxControlRefusal.None, string? Problem = null);


/// <summary>
/// A person acting on a bot's own sandbox from the sandbox screen: its state, the power controls (pause, resume,
/// rebuild, turn off), the screen, the mouse and keyboard, and a terminal. Only a person who may change the bot (an
/// owner or admin of its space) may do any of it. Every provider call goes through <see cref="SandboxBroker"/>, so
/// every limit that binds the bot binds the person, and no tenant transaction is open across one. Each action is
/// written to the audit log by kind only: never the text typed, the command, or a picture.
/// </summary>
public sealed class SandboxControlService(
    IEnumerable<SandboxBroker> brokers,
    RunScopes scopes,
    IOptions<DeploymentPolicyOptions> deployment,
    TimeProvider time,
    ILogger<SandboxControlService> logger)
{
    #region Private Fields

    private const string NoProvider = "No sandbox provider is set up on this server. The server's operator adds one.";

    private const string NoSandboxYet = "This bot has no sandbox yet. Run a command in the terminal to start one.";

    /// <summary>The longest a person's command runs, below the deployment's own limit: the request waits for it.</summary>
    private static readonly TimeSpan commandLimit = TimeSpan.FromMinutes(5);

    private readonly SandboxBroker? broker = brokers.SingleOrDefault();

    /// <summary>The screenshot being taken of each bot's sandbox, so requests that meet share one provider call.</summary>
    private readonly ConcurrentDictionary<(Guid SpaceId, Guid BotId), Lazy<Task<SandboxScreenshot>>> screens = new();

    #endregion

    #region Properties

    public const int MaxCommandCharacters = 4000;

    #endregion

    #region Public Methods

    public async Task<SandboxControlResult<BotSandboxStatus>> GetStatus(Guid spaceId, Guid userId, Guid botId, CancellationToken cancellationToken) =>
        await scopes.For(spaceId, userId).Transact(
            async services => await Check(services, spaceId, userId, botId, cancellationToken) is { } refusal
                ? Refuse<BotSandboxStatus>(refusal)
                : new(await ReadStatus(services, botId, cancellationToken)),
            cancellationToken);

    public Task<SandboxControlResult<BotSandboxStatus>> Pause(Guid spaceId, Guid userId, Guid botId, CancellationToken cancellationToken) =>
        Power(spaceId, userId, botId, "sandbox.paused", (sandbox, person) => sandbox.Pause(person, cancellationToken), cancellationToken);

    public Task<SandboxControlResult<BotSandboxStatus>> Resume(Guid spaceId, Guid userId, Guid botId, CancellationToken cancellationToken) =>
        Power(spaceId, userId, botId, "sandbox.resumed", (sandbox, person) => sandbox.Resume(person, cancellationToken), cancellationToken);

    public Task<SandboxControlResult<BotSandboxStatus>> Rebuild(Guid spaceId, Guid userId, Guid botId, CancellationToken cancellationToken) =>
        Power(
            spaceId,
            userId,
            botId,
            "sandbox.rebuilt",
            async (sandbox, person) =>
            {
                await sandbox.Rebuild(person, cancellationToken);
                return true;
            },
            cancellationToken);

    public Task<SandboxControlResult<BotSandboxStatus>> TurnOff(Guid spaceId, Guid userId, Guid botId, CancellationToken cancellationToken) =>
        Power(spaceId, userId, botId, "sandbox.turned_off", (sandbox, person) => sandbox.TurnOff(person, cancellationToken), cancellationToken);

    /// <summary>
    /// The sandbox's screen as a PNG. A paused sandbox is resumed and one not made yet is made, as on a bot's first use.
    /// Requests for the same sandbox that meet share one screenshot. Looking is not written to the audit log.
    /// </summary>
    public Task<SandboxControlResult<SandboxScreenshot>> Screen(Guid spaceId, Guid userId, Guid botId, CancellationToken cancellationToken) =>
        Control(
            spaceId,
            userId,
            botId,
            null,
            (sandbox, person) => SharedScreenshot(sandbox, person, spaceId, botId, cancellationToken),
            cancellationToken);

    /// <summary>
    /// One click, typed text, key press, or scroll, built by the same <see cref="DesktopCommands"/> a bot's tools use,
    /// with the same bounds and key list. A paused sandbox is resumed; one not made yet is not made for this.
    /// </summary>
    public async Task<SandboxControlResult<bool>> Input(
        Guid spaceId,
        Guid userId,
        Guid botId,
        SandboxInputRequest input,
        CancellationToken cancellationToken)
    {
        CommandRequest action;
        string doing;

        try
        {
            (action, doing) = BuildAction(input);
        }
        catch (BuiltinToolException wrong)
        {
            return new(false, SandboxControlRefusal.Invalid, wrong.Message);
        }

        return await Control(
            spaceId,
            userId,
            botId,
            new Audit("sandbox.input", new JsonObject { ["input"] = input.Kind }),
            async (sandbox, person) => await sandbox.Act(person, action, doing, create: false, cancellationToken) is not null
                ? true
                : throw new BuiltinToolException(NoSandboxYet),
            cancellationToken);
    }

    /// <summary>
    /// Runs a person's command line in the bot's sandbox, made on first use, for at most five minutes and never past
    /// the deployment's command limit. The command is not written to the audit log; that a command ran is.
    /// </summary>
    public async Task<SandboxControlResult<SandboxCommandResponse>> Run(
        Guid spaceId,
        Guid userId,
        Guid botId,
        string command,
        CancellationToken cancellationToken)
    {
        if (CommandProblem(command) is { } problem)
        {
            return new(null, SandboxControlRefusal.Invalid, problem);
        }

        return await Control(
            spaceId,
            userId,
            botId,
            new Audit("sandbox.command", null),
            async (sandbox, person) =>
            {
                var result = await sandbox.Run(person, command, commandLimit, cancellationToken);
                return new SandboxCommandResponse(result.ExitCode, result.Stdout, result.Stderr, result.Truncated, result.TimedOut, result.NewSandbox);
            },
            cancellationToken);
    }

    #endregion

    #region Acting

    /// <summary>A power control, then the state it left, read fresh. The audit row is written only when something changed.</summary>
    private async Task<SandboxControlResult<BotSandboxStatus>> Power(
        Guid spaceId,
        Guid userId,
        Guid botId,
        string action,
        Func<SandboxBroker, SandboxCaller, Task<bool>> work,
        CancellationToken cancellationToken)
    {
        var done = await Control(spaceId, userId, botId, new Audit(action, null), work, cancellationToken, auditWhen: changed => changed);

        return done.Refusal is SandboxControlRefusal.None
            ? await GetStatus(spaceId, userId, botId, cancellationToken)
            : new(null, done.Refusal, done.Problem);
    }

    /// <summary>
    /// The person's check in one short transaction, the work through the broker with none open, then the audit row in
    /// another. A refusal from the broker or the provider comes back as its own plain words.
    /// </summary>
    private async Task<SandboxControlResult<T>> Control<T>(
        Guid spaceId,
        Guid userId,
        Guid botId,
        Audit? audit,
        Func<SandboxBroker, SandboxCaller, Task<T>> work,
        CancellationToken cancellationToken,
        Func<T, bool>? auditWhen = null)
    {
        var tenant = scopes.For(spaceId, userId);

        if (await tenant.Transact(services => Check(services, spaceId, userId, botId, cancellationToken), cancellationToken) is { } refusal)
        {
            return Refuse<T>(refusal);
        }

        if (broker is null)
        {
            return new(default, SandboxControlRefusal.Refused, NoProvider);
        }

        T value;

        try
        {
            value = await work(broker, new SandboxCaller(tenant, botId));
        }
        catch (BuiltinToolException refused)
        {
            logger.LogInformation("A person's action on the sandbox of bot {BotId} was refused", botId);
            return new(default, SandboxControlRefusal.Refused, refused.Message);
        }

        if (audit is not null && (auditWhen?.Invoke(value) ?? true))
        {
            await tenant.Transact(
                services =>
                {
                    services.GetRequiredService<AuditLog>().Record(userId, audit.Action, AuditTargets.Bot, botId, null, null, audit.About);
                    return Task.CompletedTask;
                },
                cancellationToken);
        }

        return new(value);
    }

    private async Task<SandboxScreenshot> SharedScreenshot(
        SandboxBroker sandbox,
        SandboxCaller person,
        Guid spaceId,
        Guid botId,
        CancellationToken cancellationToken)
    {
        var key = (spaceId, botId);
        var shot = screens.GetOrAdd(key, _ => new Lazy<Task<SandboxScreenshot>>(() => TakeScreenshot(sandbox, person, key)));
        return await shot.Value.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// One screenshot, not cancelled by the request that started it, since others may be waiting on it. The provider
    /// calls under it have their own time limits.
    /// </summary>
    private async Task<SandboxScreenshot> TakeScreenshot(SandboxBroker sandbox, SandboxCaller person, (Guid SpaceId, Guid BotId) key)
    {
        try
        {
            return await sandbox.Screenshot(person, CancellationToken.None);
        }
        finally
        {
            screens.TryRemove(key, out _);
        }
    }

    #endregion

    #region Checks

    /// <summary>Null when the person may change the bot; otherwise why not. Not a member and no such bot look the same.</summary>
    private static async Task<SandboxControlRefusal?> Check(
        IServiceProvider services,
        Guid spaceId,
        Guid userId,
        Guid botId,
        CancellationToken cancellationToken)
    {
        var role = await services.GetRequiredService<AccountRepository>().FindRole(spaceId, userId, cancellationToken);

        if (role is null)
        {
            return SandboxControlRefusal.NotFound;
        }

        if (role is not (MembershipRole.Owner or MembershipRole.Admin))
        {
            return SandboxControlRefusal.NotPermitted;
        }

        return await services.GetRequiredService<BotRepository>().FindBot(botId, cancellationToken) is { Status: not BotStatus.Removed }
            ? null
            : SandboxControlRefusal.NotFound;
    }

    private static SandboxControlResult<T> Refuse<T>(SandboxControlRefusal refusal) => new(default, refusal, refusal switch
    {
        SandboxControlRefusal.NotPermitted => "Owners and admins of this space use its bots' sandboxes.",
        _ => "This bot is not here.",
    });

    private static string? CommandProblem(string command) =>
        string.IsNullOrWhiteSpace(command) ? "Type a command to run."
        : command.Length > MaxCommandCharacters ? $"A command is at most {MaxCommandCharacters} characters."
        : command.Any(character => char.IsControl(character) && character is not '\n' and not '\t')
            ? "A command cannot hold control characters other than new lines and tabs."
            : null;

    /// <summary>The action as a program and its arguments, checked as a bot's would be. A wrong one is refused here.</summary>
    private static (CommandRequest Action, string Doing) BuildAction(SandboxInputRequest input) => input.Kind switch
    {
        "click" => (
            DesktopCommands.Click(
                input.X ?? throw new BuiltinToolException("A click needs x and y."),
                input.Y ?? throw new BuiltinToolException("A click needs x and y."),
                Word(input.Button, MouseButton.Left),
                input.Twice),
            "click there"),
        "type" => (DesktopCommands.Type(input.Text ?? string.Empty), "type the text"),
        "key" => (DesktopCommands.Key(input.Keys ?? []), "press the keys"),
        "scroll" => (
            DesktopCommands.Scroll(
                Word<ScrollDirection>(input.Direction, null) ?? throw new BuiltinToolException("Scroll up or down."),
                input.Amount ?? 3,
                input.X,
                input.Y),
            "scroll"),
        _ => throw new BuiltinToolException("An input is a click, typed text, a key, or a scroll."),
    };

    private static T Word<T>(string? value, T fallback)
        where T : struct, Enum =>
        Word<T>(value, (T?)fallback) ?? fallback;

    private static T? Word<T>(string? value, T? fallback)
        where T : struct, Enum
    {
        if (value is null)
        {
            return fallback;
        }

        return Enum.GetNames<T>().FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) is { } found
            ? Enum.Parse<T>(found)
            : throw new BuiltinToolException($"{value} is not one of: {string.Join(", ", Enum.GetNames<T>().Select(name => name.ToLowerInvariant()))}.");
    }

    #endregion

    #region Reading The State

    private async Task<BotSandboxStatus> ReadStatus(IServiceProvider services, Guid botId, CancellationToken cancellationToken)
    {
        var bots = services.GetRequiredService<BotRepository>();
        var bot = await bots.FindBot(botId, cancellationToken) ?? throw new InvalidOperationException("The bot was checked in this transaction.");
        var available = broker is not null;

        if (bot.SandboxPolicy is SandboxPolicy.None)
        {
            return new BotSandboxStatus(available, HasSandbox: false, "none", null, Desktop: false, BotWorking: false);
        }

        var ownerId = SandboxGroups.OwnerOf(bot.Id, await bots.ListSandboxPath(bot.Id, cancellationToken));
        var owner = ownerId == bot.Id ? bot : await bots.FindBot(ownerId, cancellationToken) ?? bot;
        var sandboxes = services.GetRequiredService<SandboxRepository>();
        var live = await sandboxes.FindLiveForOwner(ownerId, cancellationToken);
        var replacing = await sandboxes.HasRetiringForOwner(ownerId, cancellationToken);
        var working = await services.GetRequiredService<RunRepository>().IsBotWorking(bot.Id, cancellationToken);

        return new BotSandboxStatus(
            available,
            HasSandbox: true,
            StateOf(live, replacing, time.GetUtcNow()),
            live?.LastUsedAt,
            owner.SandboxSettings.Desktop && deployment.Value.Rules.AllowDesktopSandboxes,
            working);
    }

    /// <summary>
    /// The state in a word. The provider pauses a sandbox on its own once its idle time runs out after the last use, so
    /// a running row past that time is paused, without asking the provider.
    /// </summary>
    private static string StateOf(Sandbox? live, bool replacing, DateTimeOffset now) => live switch
    {
        null when replacing => "replacing",
        null => "none",
        { State: SandboxState.Creating } => "starting",
        { State: SandboxState.Paused } => "paused",
        { ExpiresAt: { } expires } when expires <= now => "paused",
        _ => "running",
    };

    #endregion

    #region Nested Types

    /// <summary>What the audit log records for an action: its name and, at most, which kind of input it was.</summary>
    private sealed record Audit(string Action, JsonObject? About);

    #endregion
}
