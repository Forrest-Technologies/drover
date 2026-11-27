namespace Drover.Tests.Common;

/// <summary>One Remember call recorded by <see cref="FakeMemoryProvider"/>.</summary>
public sealed record RecordedRemember(MemoryScope Scope, IReadOnlyList<MemoryTurn> Turns);


/// <summary>One Recall call recorded by <see cref="FakeMemoryProvider"/>.</summary>
public sealed record RecordedRecall(MemoryScope Scope, MemoryRecallRequest Request);


/// <summary>
/// An in-memory reference <see cref="IMemoryProvider"/> that passes <c>MemoryProviderConformance</c> and lets
/// engine tests inspect every call, inject failures or delays, and assert that no tenant transaction is open during
/// any provider call.
/// </summary>
public sealed class FakeMemoryProvider(string kind = FakeMemoryProvider.DefaultKind, string? apiKey = null) : IMemoryProvider
{
    #region Private Fields

    public const string DefaultKind = "fake_memory";

    private readonly Lock gate = new();

    private readonly List<StoredEntry> entries = [];

    private readonly List<RecordedRemember> remembers = [];

    private readonly List<RecordedRecall> recalls = [];

    private readonly List<MemoryScope> forgottenConversations = [];

    private readonly List<MemoryPersonScope> forgottenPeople = [];

    private readonly List<string> sentIdentifiers = [];

    private readonly string configuredKey = apiKey ?? $"mem-fake-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}";

    #endregion

    #region Properties

    public string Kind => kind;

    public string ApiKey => configuredKey;

    /// <summary>When set, every provider call fails with this <see cref="MemoryFailure"/>.</summary>
    public MemoryFailure? FailAllWith { get; set; }

    /// <summary>When set, <see cref="Remember"/> throws this exception after recording the call.</summary>
    public Func<MemoryScope, IReadOnlyList<MemoryTurn>, Exception?>? FailRemember { get; set; }

    /// <summary>When set, <see cref="Recall"/> throws this exception or waits before returning.</summary>
    public Func<MemoryScope, MemoryRecallRequest, CancellationToken, Task<MemoryRecall?>>? OnRecall { get; set; }

    /// <summary>When set, <see cref="ForgetConversation"/> throws the returned exception when non-null.</summary>
    public Func<MemoryScope, Exception?>? FailForgetConversation { get; set; }

    /// <summary>When set, <see cref="ForgetPerson"/> throws the returned exception when non-null.</summary>
    public Func<MemoryPersonScope, Exception?>? FailForgetPerson { get; set; }

    /// <summary>Called at the start of every provider method, e.g. to check no tenant transaction is open.</summary>
    public Action<string>? BeforeCall { get; set; }

    /// <summary>Optional fixed summary returned alongside facts on recall.</summary>
    public string? Summary { get; set; }

    public IReadOnlyList<RecordedRemember> Remembers
    {
        get
        {
            lock (gate)
            {
                return [.. remembers];
            }
        }
    }

    public IReadOnlyList<RecordedRecall> Recalls
    {
        get
        {
            lock (gate)
            {
                return [.. recalls];
            }
        }
    }

    public IReadOnlyList<MemoryScope> ForgottenConversations
    {
        get
        {
            lock (gate)
            {
                return [.. forgottenConversations];
            }
        }
    }

    public IReadOnlyList<MemoryPersonScope> ForgottenPeople
    {
        get
        {
            lock (gate)
            {
                return [.. forgottenPeople];
            }
        }
    }

    public IReadOnlyList<string> SentIdentifiers
    {
        get
        {
            lock (gate)
            {
                return [.. sentIdentifiers];
            }
        }
    }

    #endregion

    #region Interface Implementations

    public Task Remember(MemoryScope scope, IReadOnlyList<MemoryTurn> turns, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeforeCall?.Invoke(nameof(Remember));
        RecordIdentifiers(scope);
        ThrowIfFailing();

        lock (gate)
        {
            remembers.Add(new RecordedRemember(scope, [.. turns]));
        }

        if (FailRemember?.Invoke(scope, turns) is { } failure)
        {
            throw failure;
        }

        lock (gate)
        {
            foreach (var turn in turns)
            {
                if (!string.IsNullOrWhiteSpace(turn.Text))
                {
                    entries.Add(new StoredEntry(scope, turn.Text.Trim(), turn.At));
                }
            }
        }

        return Task.CompletedTask;
    }

    public async Task<MemoryRecall?> Recall(MemoryScope scope, MemoryRecallRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeforeCall?.Invoke(nameof(Recall));
        RecordIdentifiers(scope);
        ThrowIfFailing();

        lock (gate)
        {
            recalls.Add(new RecordedRecall(scope, request));
        }

        if (OnRecall is { } custom)
        {
            return await custom(scope, request, cancellationToken);
        }

        List<string> matched;

        lock (gate)
        {
            matched =
            [
                .. entries
                    .Where(entry => entry.Scope.SpaceId == scope.SpaceId
                        && entry.Scope.BotId == scope.BotId
                        && entry.Scope.PersonId == scope.PersonId)
                    .Select(entry => entry.Text)
                    .Distinct(StringComparer.Ordinal),
            ];
        }

        return Fit(matched, Summary, request.MaxCharacters);
    }

    public Task ForgetConversation(MemoryScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeforeCall?.Invoke(nameof(ForgetConversation));
        RecordIdentifiers(scope);
        ThrowIfFailing();

        lock (gate)
        {
            forgottenConversations.Add(scope);
        }

        if (FailForgetConversation?.Invoke(scope) is { } failure)
        {
            throw failure;
        }

        lock (gate)
        {
            entries.RemoveAll(entry => entry.Scope.SpaceId == scope.SpaceId
                && entry.Scope.BotId == scope.BotId
                && entry.Scope.PersonId == scope.PersonId
                && entry.Scope.ThreadId == scope.ThreadId);
        }

        return Task.CompletedTask;
    }

    public Task ForgetPerson(MemoryPersonScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeforeCall?.Invoke(nameof(ForgetPerson));
        RecordIdentifiers(scope);
        ThrowIfFailing();

        lock (gate)
        {
            forgottenPeople.Add(scope);
        }

        if (FailForgetPerson?.Invoke(scope) is { } failure)
        {
            throw failure;
        }

        lock (gate)
        {
            entries.RemoveAll(entry => entry.Scope.SpaceId == scope.SpaceId
                && entry.Scope.PersonId == scope.PersonId);
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Private Methods

    private void RecordIdentifiers(MemoryScope scope)
    {
        lock (gate)
        {
            sentIdentifiers.Add(scope.SpaceId.ToString("D"));
            sentIdentifiers.Add(scope.BotId.ToString("D"));
            sentIdentifiers.Add(scope.PersonId.ToString("D"));
            sentIdentifiers.Add(scope.ThreadId.ToString("D"));
        }
    }

    private void RecordIdentifiers(MemoryPersonScope scope)
    {
        lock (gate)
        {
            sentIdentifiers.Add(scope.SpaceId.ToString("D"));
            sentIdentifiers.Add(scope.PersonId.ToString("D"));
        }
    }

    private void ThrowIfFailing()
    {
        if (FailAllWith is not { } failure)
        {
            return;
        }

        throw failure switch
        {
            MemoryFailure.InvalidKey => MemoryProviderException.FromStatus(401),
            MemoryFailure.RetryLater => MemoryProviderException.FromStatus(429, TimeSpan.FromSeconds(5)),
            MemoryFailure.OutOfCredit => MemoryProviderException.FromStatus(402),
            MemoryFailure.Unreachable => MemoryProviderException.FromNetwork(),
            _ => MemoryProviderException.FromStatus(400),
        };
    }

    private static MemoryRecall? Fit(List<string> facts, string? summary, int maxCharacters)
    {
        var room = Math.Max(0, maxCharacters);
        string? keptSummary = null;

        if (!string.IsNullOrWhiteSpace(summary) && room > 0)
        {
            var trimmed = summary.Trim();
            keptSummary = trimmed.Length <= room ? trimmed : trimmed[..room];
            room -= keptSummary.Length;
        }

        List<string> kept = [];

        foreach (var fact in facts)
        {
            if (fact.Length > room)
            {
                break;
            }

            kept.Add(fact);
            room -= fact.Length;
        }

        return kept.Count == 0 && keptSummary is null ? null : new MemoryRecall(kept, keptSummary);
    }

    #endregion

    #region Nested Types

    private sealed record StoredEntry(MemoryScope Scope, string Text, DateTimeOffset At);

    #endregion
}
