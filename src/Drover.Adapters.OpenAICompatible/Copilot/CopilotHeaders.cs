namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// The editor and integration headers Copilot's chat API requires, and the shorter set GitHub's device-code
/// endpoints accept. Applied on every outbound request so a call cannot forget one.
/// </summary>
internal static class CopilotHeaders
{
    #region Public Methods

    public static void Apply(HttpRequestMessage request, CopilotPlanOptions options, CopilotCall? call = null)
    {
        foreach (var (name, value) in For(request.RequestUri, options, call ?? CopilotCall.Current))
        {
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    public static IReadOnlyList<(string Name, string Value)> For(Uri? address, CopilotPlanOptions options, CopilotCall? call)
    {
        List<(string Name, string Value)> headers = [("User-Agent", options.UserAgent)];

        if (address is null || IsGitHubLogin(address))
        {
            return headers;
        }

        headers.Add(("Editor-Version", options.EditorVersion));
        headers.Add(("Editor-Plugin-Version", options.EditorPluginVersion));
        headers.Add(("Copilot-Integration-Id", options.IntegrationId));

        if (IsModelsList(address))
        {
            headers.Add(("X-GitHub-Api-Version", options.ApiVersion));
        }

        if (IsChat(address))
        {
            headers.Add(("X-Initiator", call is { FromPerson: true } ? "user" : "agent"));
            headers.Add(("Openai-Intent", "conversation-edits"));

            if (call is { HasImage: true })
            {
                headers.Add(("Copilot-Vision-Request", "true"));
            }
        }

        return headers;
    }

    #endregion

    #region Private Methods

    private static bool IsGitHubLogin(Uri address) =>
        address.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && !address.AbsolutePath.Contains("copilot_internal", StringComparison.OrdinalIgnoreCase);

    private static bool IsModelsList(Uri address) =>
        address.AbsolutePath.TrimEnd('/').EndsWith("/models", StringComparison.OrdinalIgnoreCase);

    private static bool IsChat(Uri address) =>
        address.AbsolutePath.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase);

    #endregion
}
