namespace Drover.Domain.Models;

/// <summary>
/// Where the person goes to approve a sign-in, the code they enter there when the flow has one, and how the answer
/// comes back.
/// </summary>
public sealed record ModelSignInStep(Uri Address, string? Code, ModelSignInWait Wait);
