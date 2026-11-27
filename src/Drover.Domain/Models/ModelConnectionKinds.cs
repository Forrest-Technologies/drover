namespace Drover.Domain.Models;

/// <summary>The names of the kinds of model connection that more than one part of Drover has to recognize.</summary>
public static class ModelConnectionKinds
{
    #region Constants

    /// <summary>
    /// Any server that speaks the common chat protocol. It lists models and nothing more about them, so what a model
    /// can do beyond tools and streaming is what the operator says.
    /// </summary>
    public const string Compatible = "openai_compatible";

    /// <summary>
    /// The server's own speech service, named in the server's configuration. Never the kind of a stored connection:
    /// no space can make one.
    /// </summary>
    public const string ServerSpeech = "server_speech";

    /// <summary>
    /// A person's own GitHub Copilot plan. Each person signs in with the operator's registered GitHub OAuth app;
    /// there is no key and no address to paste.
    /// </summary>
    public const string CopilotSignIn = "copilot_signin";

    #endregion
}
