namespace Drover.Engine.Tools;

/// <summary>
/// The tokens of one credential of a sign-in connection, kept in the vault. A tool source reads them before a
/// request and writes them after a sign-in or a renewal; each is a short transaction of its own, as one person in
/// one space, so row-level security decides whose tokens come back. The text is the source's own and is not
/// interpreted here, except to find the values that must be scrubbed from whatever the tool server returns.
/// </summary>
public sealed class KeptToolTokens(RunTenant tenant, Guid connectionId, Guid audienceUserId)
{
    #region Private Fields

    /// <summary>A value shorter than this is a word such as a token type, not a credential.</summary>
    private const int ShortestCredential = 16;

    private readonly ConcurrentQueue<string> secrets = new();

    #endregion

    #region Properties

    /// <summary>Every credential this object has seen, including tokens renewed while it was in use.</summary>
    public IReadOnlyCollection<string> Secrets => secrets;

    #endregion

    #region Public Methods

    public Task<string?> Read(CancellationToken cancellationToken) =>
        tenant.Transact(
            async services =>
            {
                var credential = await services.GetRequiredService<ToolConnectionRepository>().GetCredential(connectionId, audienceUserId, cancellationToken);

                if (credential is null)
                {
                    return null;
                }

                var stored = await services.GetRequiredService<ISecretVault>().Reveal(credential.SecretId, cancellationToken);
                NoteTokens(stored);
                return stored;
            },
            cancellationToken);

    public Task Write(string tokens, DateTimeOffset? runsOutAt, CancellationToken cancellationToken) =>
        tenant.Transact(
            async services =>
            {
                NoteTokens(tokens);
                var connections = services.GetRequiredService<ToolConnectionRepository>();
                var vault = services.GetRequiredService<ISecretVault>();
                var credential = await connections.GetCredential(connectionId, audienceUserId, cancellationToken);

                if (credential is null)
                {
                    var secretId = await vault.Store(
                        SecretKind.OauthTokens,
                        tokens,
                        audienceUserId == Audience.Everyone ? null : audienceUserId,
                        cancellationToken);
                    connections.Add(
                        new ToolConnectionCredential { ToolConnectionId = connectionId, SecretId = secretId, AudienceUserId = audienceUserId, ExpiresAt = runsOutAt });
                }
                else
                {
                    await vault.Replace(credential.SecretId, tokens, cancellationToken);
                    credential.Status = CredentialStatus.Active;
                    credential.ExpiresAt = runsOutAt;
                }
            },
            cancellationToken);

    public void Note(string? secret)
    {
        if (secret is { Length: >= ShortestCredential })
        {
            secrets.Enqueue(secret);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>Finds the credentials in a token set without knowing its shape: every text value under a name that says token or secret.</summary>
    private void NoteTokens(string tokens)
    {
        if (!tokens.AsSpan().TrimStart().StartsWith("{"))
        {
            // Not a shape this can read: scrub the whole text, which is still a credential.
            Note(tokens);
            return;
        }

        using var document = JsonDocument.Parse(tokens);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.String
                && (property.Name.Contains("token", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("secret", StringComparison.OrdinalIgnoreCase)))
            {
                Note(property.Value.GetString());
            }
        }
    }

    #endregion
}
