namespace Drover.Engine.Sandboxes;

/// <summary>How a sandbox secret is shown: never with its value.</summary>
public static class SandboxSecretViews
{
    #region Public Methods

    /// <summary>An enumeration value as the API spells it. Every value a sandbox secret uses is one lowercase word.</summary>
    public static string Wire<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();

    public static bool TryParse<TEnum>(string? text, out TEnum value)
        where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(Wire(candidate), text, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    public static SandboxSecretResponse ToResponse(
        this SandboxSecretWithBots entry,
        IReadOnlyDictionary<Guid, string> botNames,
        string? projectName = null)
    {
        var secret = entry.Secret;
        return new(
            secret.Id,
            secret.Name,
            secret.Description,
            Wire(secret.Mode),
            secret.Hosts,
            secret.AllowInQuery,
            secret.AllowInBody,
            Wire(secret.Scope),
            [.. entry.BotIds.Select(id => new SandboxSecretBotRef(id, botNames.GetValueOrDefault(id) ?? string.Empty))],
            secret.ProjectId,
            projectName,
            secret.CreatedByUserId,
            secret.CreatedAt,
            secret.RotatedAt,
            secret.LastUsedAt);
    }

    #endregion
}
