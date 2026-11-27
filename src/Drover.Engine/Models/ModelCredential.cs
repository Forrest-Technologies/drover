namespace Drover.Engine.Models;

/// <summary>Why a model call has no credential to send.</summary>
public enum ModelCredentialProblem
{
    /// <summary>The stored key or token set could not be read: the key ring changed, or the secret is gone.</summary>
    Unreadable,

    /// <summary>The connection is one each person signs in to, and the person the call acts for has not.</summary>
    SignInMissing,

    /// <summary>The provider ended the person's sign-in. They must sign in again.</summary>
    SignInEnded,

    /// <summary>This server does not allow the connection's sign-in kind.</summary>
    NotAvailable,

    /// <summary>The sign-in needed renewing and the provider could not be reached. It is as good as it was.</summary>
    Unreachable,
}

/// <summary>
/// What a model call sends as its credential: a connection's key, a person's access token, nothing for a connection
/// that needs none, or why there is none. <see cref="ModelCredentials"/> makes it; it prints nothing of the value.
/// </summary>
public sealed record ModelCredential(string? Value, ModelCredentialProblem? Problem, string? Message)
{
    #region Properties

    public static ModelCredential None { get; } = new(null, null, null);

    /// <summary>The person's sign-in this credential came from, or null for a key. <see cref="ModelCredentials.Ready"/> renews through it.</summary>
    public ModelSignInHandle? SignIn { get; init; }

    /// <summary>When the access token runs out, or null when it has no end.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>What the person's sign-in says about calling the provider (<see cref="ModelTokens.Details"/>). Empty for a key.</summary>
    public IReadOnlyDictionary<string, string> Details { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    #endregion

    #region Public Methods

    /// <summary>The endpoint a call with this credential goes to: <paramref name="endpoint"/> with the sign-in's details.</summary>
    public ModelEndpoint For(ModelEndpoint endpoint) => Details.Count == 0 ? endpoint : endpoint with { SignInDetails = Details };

    public static ModelCredential Of(string? value) => new(value, null, null);

    public static ModelCredential Refused(ModelCredentialProblem problem, string message) => new(null, problem, message);

    public override string ToString() => $"{nameof(ModelCredential)} {Problem?.ToString() ?? "[secret]"}";

    #endregion
}

/// <summary>A model call could not be given a credential. Its <see cref="Credential"/> says why, in words for the person.</summary>
public sealed class ModelCredentialException(ModelCredential credential) : Exception(credential.Message)
{
    #region Properties

    public ModelCredential Credential { get; } = credential;

    #endregion
}

/// <summary>
/// Which sign-in a renewal works on: ids only, and the kind whose adapter renews it. <paramref name="SecretId"/> is the
/// token set the credential was read with, so an ending that follows only ends that set, not a sign-in made since.
/// </summary>
public sealed record ModelSignInHandle(Guid SpaceId, Guid UserId, Guid ConnectionId, Guid CredentialId, string Kind, string ConnectionName, Guid? SecretId = null);
