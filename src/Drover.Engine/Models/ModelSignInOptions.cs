namespace Drover.Engine.Models;

/// <summary>
/// What the operator sets about signing in to model providers, under <c>Drover:Models</c>. <see cref="SignInOff"/>
/// lists the sign-in kinds switched off on this server, so a kind can be turned off without a release when a
/// provider changes its terms or its flow. Empty by default.
/// </summary>
public sealed class ModelSignInOptions
{
    #region Properties

    public List<string> SignInOff { get; set; } = [];

    #endregion
}
