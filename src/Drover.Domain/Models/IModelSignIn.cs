namespace Drover.Domain.Models;

/// <summary>
/// Signs one person in to a model provider with their own account, in place of an API key. One implementation per
/// sign-in kind, in that provider's adapter project. A kind is a sign-in kind because one of these is registered for
/// it; no column on the connection says so. Every token set it returns is the signing-in person's own and goes to the
/// vault, private to them. Nothing here may put a token in an exception message.
/// </summary>
public interface IModelSignIn
{
    /// <summary>The connection kind this sign-in serves, for example "openai_signin".</summary>
    string Kind { get; }

    /// <summary>What a person calls it, for example "ChatGPT plan". A connection a sign-in makes is given this name.</summary>
    string DisplayName { get; }

    /// <summary>
    /// True when the sign-in uses a person's own paid plan. The deployment rule <c>SubscriptionSignIn</c> turns such
    /// kinds off on a hosted server. False for a flow the provider offers to any app, which stays on.
    /// </summary>
    bool IsPersonalPlan { get; }

    /// <summary>
    /// Whether the person's device may receive the browser's return at this address. A kind compares it with the
    /// address it is configured for, where only the port may vary. Null is a device that cannot listen.
    /// </summary>
    bool AcceptsRedirect(Uri? redirectUri);

    /// <summary>Where the person goes to approve, and how the answer will come back.</summary>
    Task<ModelSignInStep> Begin(ModelSignInAttempt attempt, CancellationToken cancellation);

    /// <summary>
    /// How long to wait before the next poll of a waiting attempt, when <see cref="Begin"/> returned
    /// <see cref="ModelSignInWait.Poll"/>. The default is five seconds. A provider that was told to slow down
    /// returns a longer wait.
    /// </summary>
    TimeSpan PollWait(ModelSignInAttempt attempt) => TimeSpan.FromSeconds(5);

    /// <summary>
    /// Exchanges what the person's device received for a token set. Throws <see cref="ModelSignInRefusedException"/>
    /// when the answer is a refusal or does not belong to the attempt; nothing is exchanged then. For a poll, a
    /// null return means the person has not approved yet and the caller should wait and ask again.
    /// </summary>
    Task<ModelTokens?> Finish(ModelSignInAttempt attempt, ModelSignInReturn arrived, CancellationToken cancellation);

    /// <summary>
    /// A new token set for the current one. Throws <see cref="SignInEndedException"/> when the provider says the
    /// sign-in is over, and <see cref="ModelProviderException"/> for anything that may pass, such as a network failure.
    /// </summary>
    Task<ModelTokens> Refresh(ModelTokens current, CancellationToken cancellation);

    /// <summary>Tells the provider the sign-in is no longer wanted, where the provider has a way to be told.</summary>
    Task Revoke(ModelTokens current, CancellationToken cancellation);
}
