namespace Drover.Domain.Models;

/// <summary>
/// A sign-in did not end with an approval: the person refused, the provider refused, or what came back does not
/// belong to the attempt. Nothing was exchanged. The message is Drover's own and never holds anything the provider sent.
/// </summary>
public sealed class ModelSignInRefusedException() : Exception("Sign-in was cancelled.");
