namespace Drover.Adapters.E2B;

// The request and response shapes Drover uses, from E2B's spec/openapi.yml and the envd proto files. They never
// leave this project. Envd's RPC uses the proto3 JSON mapping: zero values are left out, int64 is a string, bytes are
// base64, enums are names, and times are RFC 3339 with up to nine fraction digits.

/// <summary>POST /v2/sandboxes (NewSandboxV2).</summary>
internal sealed record NewSandboxRequest(
    [property: JsonPropertyName("templateID")] string TemplateId,
    int Timeout,
    bool AutoPause,
    bool AutoPauseMemory,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyDictionary<string, string>? EnvVars,
    NetworkConfig Network);

/// <summary>SandboxNetworkConfig on create, SandboxNetworkUpdateConfig on PUT /sandboxes/{id}/network.</summary>
internal sealed record NetworkConfig(
    IReadOnlyList<string>? AllowOut,
    IReadOnlyList<string>? DenyOut,
    bool? AllowPublicTraffic);

/// <summary>The Sandbox answer of create and connect.</summary>
internal sealed record SandboxReply(
    [property: JsonPropertyName("sandboxID")] string SandboxId,
    string? EnvdAccessToken,
    string? EnvdVersion);

/// <summary>GET /sandboxes/{id} (SandboxDetail). The times are read with <see cref="ProtoTime"/>.</summary>
internal sealed record SandboxDetailReply(
    [property: JsonPropertyName("sandboxID")] string SandboxId,
    string? State,
    IReadOnlyDictionary<string, string>? Metadata,
    string? EndAt);

/// <summary>POST /sandboxes/{id}/timeout.</summary>
internal sealed record TimeoutRequest(int Timeout);

/// <summary>POST /v2/sandboxes/{id}/connect (ConnectSandboxV2).</summary>
internal sealed record ConnectSandboxRequest(int Timeout, bool? Memory);

/// <summary>process.StartRequest.</summary>
internal sealed record StartRequest(ProcessConfig Process, bool Stdin);

internal sealed record ProcessConfig(
    string Cmd,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Envs,
    string? Cwd);

/// <summary>process.SendSignalRequest.</summary>
internal sealed record SendSignalRequest(ProcessSelector Process, string Signal);

internal sealed record ProcessSelector(int Pid);

/// <summary>filesystem.ListDirRequest.</summary>
internal sealed record ListDirRequest(string Path, int Depth);

/// <summary>filesystem.ListDirResponse.</summary>
internal sealed record ListDirReply(IReadOnlyList<EntryInfo>? Entries);

/// <summary>filesystem.EntryInfo. Size is an int64, which proto3 JSON sends as a string.</summary>
internal sealed record EntryInfo(
    string Name,
    string? Type,
    string Path,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long Size,
    string? ModifiedTime);

/// <summary>What an error answer said that Drover acts on. The provider's message text is never passed on.</summary>
internal sealed record ProviderError(int Status, string? Code, bool? ShouldRetry, TimeSpan? RetryAfter);

internal static class Wire
{
    #region Properties

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    #endregion
}

/// <summary>Reads proto3 JSON and RFC 3339 times, which carry up to nine fraction digits where .NET reads seven.</summary>
internal static partial class ProtoTime
{
    #region Public Methods

    public static DateTimeOffset? Parse(string? value) =>
        value is not null && DateTimeOffset.TryParse(
            ExtraFractionDigits().Replace(value, "$1"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;

    #endregion

    #region Private Methods

    [GeneratedRegex(@"(\.\d{7})\d+")]
    private static partial Regex ExtraFractionDigits();

    #endregion
}
