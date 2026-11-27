namespace Drover.Domain.Models;

/// <summary>
/// The provider says a sign-in is over: the refresh token was revoked, ran out, or was already spent. Signing in
/// again is the only way on. The message is Drover's own and never holds anything the provider sent.
/// </summary>
public sealed class SignInEndedException() : Exception("The sign-in has ended. Sign in again.");
