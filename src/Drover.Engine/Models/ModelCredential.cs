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

    /// <summary>A sign-in whose access token runs out soon, to be renewed before the call, outside any transaction.</summary>
    public ModelSignInHandle? Renewal { get; init; }

    #endregion

    #region Public Methods

    public static ModelCredential Of(string? value) => new(value, null, null);

    public static ModelCredential Refused(ModelCredentialProblem problem, string message) => new(null, problem, message);

    public override string ToString() => $"{nameof(ModelCredential)} {Problem?.ToString() ?? "[secret]"}";

    #endregion
}

/// <summary>Which sign-in a renewal works on: ids only, and the kind whose adapter renews it.</summary>
public sealed record ModelSignInHandle(Guid SpaceId, Guid UserId, Guid ConnectionId, Guid CredentialId, string Kind, string ConnectionName);
