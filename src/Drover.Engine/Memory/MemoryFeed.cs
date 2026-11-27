namespace Drover.Engine.Memory;

/// <summary>
/// Connects Drover's run lifecycle and forget hooks to the configured <see cref="IMemoryProvider"/>.
/// Every provider call runs outside any tenant transaction, under the space's outbound policy, and never puts
/// message text, recalled facts, or secrets in any log line.
/// </summary>
public sealed class MemoryFeed(
    IEnumerable<IMemoryProvider> providers,
    IOptions<MemoryOptions> options,
    IOptions<DeploymentPolicyOptions> deployment,
    TimeProvider time,
    ILogger<MemoryFeed> logger)
{
    #region Properties

    /// <summary>
    /// The active memory provider, or null when <see cref="DeploymentPolicy.AllowMemoryService"/> is false, no
    /// provider is configured, or no registered provider matches <see cref="MemoryOptions.Provider"/>.
    /// </summary>
    public IMemoryProvider? ActiveProvider
    {
        get
        {
            if (!deployment.Value.Rules.AllowMemoryService)
            {
                return null;
            }

            var configured = options.Value.Provider?.Trim();

            if (string.IsNullOrEmpty(configured))
            {
                return null;
            }

            return providers.FirstOrDefault(provider =>
                string.Equals(provider.Kind, configured, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool IsActive => ActiveProvider is not null;

    #endregion

    #region Public Methods

    /// <summary>
    /// Sends the person's triggering message and the bot's final reply (unless the run was tainted) from a completed
    /// private conversation to the configured memory provider. Runs after the run commits, with no tenant transaction
    /// open. Failures are logged without message text or secrets and never change the run's outcome.
    /// </summary>
    public async Task Remember(RunTenant tenant, Run completedRun, CancellationToken cancellationToken)
    {
        if (ActiveProvider is not { } provider)
        {
            return;
        }

        try
        {
            if (await tenant.Transact(services => PrepareTurns(services, completedRun.Id, cancellationToken), cancellationToken)
                is not { } pending)
            {
                return;
            }

            EnsureNoOpenTransaction();
            using var outbound = OutboundAttempt.Begin(pending.Policy);
            await provider.Remember(pending.Scope, pending.Turns, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Memory service {Provider} remember for run {RunId} stopped with the server", provider.Kind, completedRun.Id);
        }
        catch (Exception failure)
        {
            // Never log exception messages or message content: only provider kind, run id, and failure kind.
            logger.LogWarning(
                "Memory service {Provider} failed to remember run {RunId}: {Failure}",
                provider.Kind,
                completedRun.Id,
                FailureKind(failure));
        }
    }

    /// <summary>
    /// Asks the configured memory provider what it knows about the run's person for this bot, bounded by
    /// <see cref="MemoryOptions.RecallMaxCharacters"/> and <see cref="MemoryOptions.RecallTimeoutSeconds"/>, with no
    /// tenant transaction open. A timeout or failure returns no memory and a <see cref="MemoryServiceUnavailable"/>
    /// payload so the run continues normally.
    /// </summary>
    public async Task<(RecalledServiceMemory? Memory, MemoryServiceUnavailable? Unavailable)> Recall(
        MemoryScope scope,
        EffectivePolicy policy,
        string? latestMessage,
        Guid runId,
        CancellationToken cancellationToken)
    {
        if (ActiveProvider is not { } provider)
        {
            return (null, null);
        }

        EnsureNoOpenTransaction();
        var maxCharacters = options.Value.RecallMaxCharacters;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.RecallTimeoutSeconds), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            using var outbound = OutboundAttempt.Begin(policy);
            var recalled = await provider.Recall(scope, new MemoryRecallRequest(latestMessage, maxCharacters), linked.Token);

            if (recalled is null || (recalled.Facts.Count == 0 && string.IsNullOrWhiteSpace(recalled.Summary)))
            {
                return (null, null);
            }

            return (new RecalledServiceMemory(recalled.Facts, recalled.Summary, maxCharacters), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Memory service {Provider} timed out during recall for run {RunId}", provider.Kind, runId);
            return (null, new MemoryServiceUnavailable(provider.Kind, "timeout"));
        }
        catch (Exception failure)
        {
            var reason = FailureReason(failure);
            logger.LogWarning(
                "Memory service {Provider} failed during recall for run {RunId}: {Failure}",
                provider.Kind,
                runId,
                reason);
            return (null, new MemoryServiceUnavailable(provider.Kind, reason));
        }
    }

    /// <summary>
    /// Sends one pending forget request to the active provider outside any tenant transaction, and deletes the
    /// durable row on success. Returns true when the provider accepted the forget.
    /// </summary>
    public async Task<bool> TryForget(
        RunScopes scopes,
        MemoryForgetRequest request,
        EffectivePolicy policy,
        CancellationToken cancellationToken)
    {
        if (ActiveProvider is not { } provider)
        {
            return false;
        }

        EnsureNoOpenTransaction();

        try
        {
            using var outbound = OutboundAttempt.Begin(policy);

            if (request.Kind is MemoryForgetKind.Conversation)
            {
                await provider.ForgetConversation(
                    new MemoryScope(request.SpaceId, request.BotId!.Value, request.PersonId, request.ThreadId!.Value),
                    cancellationToken);
            }
            else
            {
                await provider.ForgetPerson(
                    new MemoryPersonScope(request.SpaceId, request.PersonId),
                    cancellationToken);
            }

            await scopes.For(request.SpaceId, request.PersonId).Transact(
                services => services.GetRequiredService<MemoryForgetRepository>().RemoveById(request.Id, CancellationToken.None),
                CancellationToken.None);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            logger.LogWarning(
                "Memory service {Provider} failed to process forget request {RequestId} ({Kind}): {Failure}",
                provider.Kind,
                request.Id,
                request.Kind,
                FailureKind(failure));
            return false;
        }
    }

    /// <summary>Only spoken text parts. Tool calls, tool results, files, pictures, reasoning, and citations are excluded.</summary>
    public static string SpokenText(Message message) =>
        string.Join("\n", message.Parts.OfType<TextPart>().Select(part => part.Text)).Trim();

    #endregion

    #region Private Methods

    private static async Task<PendingRemember?> PrepareTurns(
        IServiceProvider services,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await services.GetRequiredService<RunRepository>().FindRun(runId, cancellationToken);

        if (run is not { Status: RunStatus.Completed, ParentRunId: null })
        {
            return null;
        }

        var thread = await services.GetRequiredService<ThreadRepository>().FindThread(run.ThreadId, cancellationToken);

        if (thread is null
            || thread.Origin is ThreadOrigin.Delegation
            || thread.AudienceUserId != run.PrincipalUserId)
        {
            return null;
        }

        var bot = await services.GetRequiredService<BotRepository>().FindBot(run.BotId, cancellationToken);

        if (bot is not { MemorySettings.Enabled: true })
        {
            return null;
        }

        var messages = services.GetRequiredService<MessageRepository>();
        List<MemoryTurn> turns = [];

        if (run.TriggerMessageId is { } triggerId
            && await messages.FindMessage(triggerId, cancellationToken) is { Role: MessageRole.User, Withheld: false, ShelvedAt: null } trigger)
        {
            var text = SpokenText(trigger);

            if (text.Length > 0)
            {
                turns.Add(new MemoryTurn(MemorySpeaker.Person, text, trigger.CreatedAt));
            }
        }

        if (!run.Tainted
            && await messages.FindFinalReplyForRun(thread.Id, run.Id, cancellationToken) is { } reply)
        {
            var text = SpokenText(reply);

            if (text.Length > 0)
            {
                turns.Add(new MemoryTurn(MemorySpeaker.Bot, text, reply.CreatedAt));
            }
        }

        if (turns.Count == 0)
        {
            return null;
        }

        var policy = await services.GetRequiredService<PolicyService>().GetEffective(run.SpaceId, cancellationToken);
        var scope = new MemoryScope(run.SpaceId, bot.Id, run.PrincipalUserId, thread.Id);
        return new PendingRemember(scope, turns, policy);
    }

    private static void EnsureNoOpenTransaction()
    {
        if (TenantScope.IsOpenInCurrentFlow)
        {
            throw new InvalidOperationException("A memory provider was called inside a tenant transaction. Close the scope before the call.");
        }
    }

    private static string FailureKind(Exception failure) =>
        failure is MemoryProviderException providerFailure ? providerFailure.Failure.ToString() : failure.GetType().Name;

    private static string FailureReason(Exception failure) =>
        failure is MemoryProviderException providerFailure
            ? providerFailure.Failure switch
            {
                MemoryFailure.InvalidKey => "invalid_key",
                MemoryFailure.RetryLater => "retry_later",
                MemoryFailure.OutOfCredit => "out_of_credit",
                MemoryFailure.Unreachable => "unreachable",
                _ => "rejected",
            }
            : "failed";

    #endregion

    #region Nested Types

    private sealed record PendingRemember(MemoryScope Scope, IReadOnlyList<MemoryTurn> Turns, EffectivePolicy Policy);

    #endregion
}
