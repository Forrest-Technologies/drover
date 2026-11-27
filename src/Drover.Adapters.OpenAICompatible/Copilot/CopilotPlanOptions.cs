namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// What the Copilot plan adapter sends to GitHub. The client id is the public Copilot client id, which is the
/// one GitHub authorizes to exchange a GitHub token for a Copilot session token. It is not a secret. An operator
/// who needs another id sets <c>Drover:Models:Copilot:ClientId</c>.
/// </summary>
public sealed class CopilotPlanOptions
{
    #region Private Fields

    public const string Section = "Drover:Models:Copilot";

    /// <summary>The public Copilot client id. It is not a secret.</summary>
    public const string DefaultClientId = "Iv1.b507a08c87ecfe98";

    #endregion

    #region Properties

    public string ClientId { get; set; } = DefaultClientId;

    public Uri DeviceCodeAddress { get; set; } = new("https://github.com/login/device/code");

    public Uri TokenAddress { get; set; } = new("https://github.com/login/oauth/access_token");

    public Uri ExchangeAddress { get; set; } = new("https://api.github.com/copilot_internal/v2/token");

    public Uri ApiAddress { get; set; } = new("https://api.individual.githubcopilot.com/");

    public string Scope { get; set; } = "read:user";

    public string UserAgent { get; set; } = "GitHubCopilotChat/0.35.0";

    public string EditorVersion { get; set; } = "vscode/1.107.0";

    public string EditorPluginVersion { get; set; } = "copilot-chat/0.35.0";

    public string IntegrationId { get; set; } = "vscode-chat";

    public string ApiVersion { get; set; } = "2026-06-01";

    #endregion
}
