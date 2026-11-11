namespace Drover.Domain.Policy;

/// <summary>
/// Everything that differs between a server for one household and a service for strangers, as one typed value
/// per rule. Feature code asks one rule a specific question and never asks which preset is selected. A property
/// without an entry in <see cref="Rules"/> and a row in the matrix test fails the build.
/// </summary>
public sealed record DeploymentPolicy
{
    #region Constants

    public const string SelfHostedName = "self_hosted";

    public const string HostedName = "hosted";

    #endregion

    #region Presets

    public static DeploymentPolicy SelfHosted { get; } = new();

    public static DeploymentPolicy Hosted { get; } = SelfHosted.AsHosted();

    /// <summary>The preset with that name. False, with the self-hosted preset, when there is no such preset.</summary>
    public static bool TryGetPreset(string? name, out DeploymentPolicy preset)
    {
        preset = name == HostedName ? Hosted : SelfHosted;
        return name is SelfHostedName or HostedName;
    }

    #endregion

    #region Rules

    /// <summary>Outbound requests to private and link-local addresses (model base URLs, MCP URLs, OAuth discovery).</summary>
    public bool AllowPrivateAddresses { get; init; } = true;

    public SandboxAdapterSet SandboxAdapters { get; init; } = SandboxAdapterSet.All;

    /// <summary>Local-process (stdio) MCP servers on the host itself. Never allowed, in any preset.</summary>
    public bool AllowHostStdioMcp { get; init; }

    public SubscriptionSignInMode SubscriptionSignIn { get; init; } = SubscriptionSignInMode.WhereProviderPermits;

    public ModelProviderSet ModelProviders { get; init; } = ModelProviderSet.All;

    /// <summary>Anyone may register. Off in both presets; the operator of a hosted service may turn it on.</summary>
    public bool OpenRegistration { get; init; }

    public bool RequireEmailVerification { get; init; }

    public bool RequireTermsAcceptance { get; init; }

    /// <summary>First-run setup needs the one-time code from the server log, from every address. On in both presets.</summary>
    public bool RequireSetupCode { get; init; } = true;

    public QuotaMode SpaceQuotas { get; init; } = QuotaMode.Unlimited;

    /// <summary>A space may turn tainted-run escalation off. It is on unless a space does.</summary>
    public bool SpaceMayDisableTaintedRunEscalation { get; init; } = true;

    /// <summary>A space may turn on loading remote images in bot output. It is off unless a space does.</summary>
    public bool SpaceMayAutoLoadRemoteImages { get; init; } = true;

    public KeyRingProtection KeyRingProtection { get; init; } = KeyRingProtection.FilePermissions;

    /// <summary>The credential that can bypass row-level security is present in the process that serves requests.</summary>
    public bool AllowMigrationCredentialInServer { get; init; } = true;

    public AuditRetentionMode AuditRetention { get; init; } = AuditRetentionMode.OperatorChoice;

    /// <summary>Data export and account deletion may be switched off.</summary>
    public bool AllowDisablingDataRights { get; init; } = true;

    /// <summary>Message content may be part of telemetry. Never, in any preset.</summary>
    public bool AllowTelemetryContent { get; init; }

    /// <summary>The operator may read tenant content through the product. Never, in any preset.</summary>
    public bool AllowOperatorContentAccess { get; init; }

    #endregion

    #region Rule Table

    /// <summary>Every rule, in the order the status listing shows them.</summary>
    public static IReadOnlyList<PolicyRule> Rules { get; } =
    [
        PolicyRule.Permission(
            nameof(AllowPrivateAddresses),
            policy => policy.AllowPrivateAddresses,
            Enforcement.Now("OutboundHttp, on every outbound connection")),
        PolicyRule.Choice(
            nameof(SandboxAdapters),
            policy => policy.SandboxAdapters,
            Enforcement.Later("Phase 1: sandbox adapter registration")),
        PolicyRule.Permission(
            nameof(AllowHostStdioMcp),
            policy => policy.AllowHostStdioMcp,
            Enforcement.Later("Phase 1: MCP tool servers (06-mcp.md)")),
        PolicyRule.Choice(
            nameof(SubscriptionSignIn),
            policy => policy.SubscriptionSignIn,
            Enforcement.Later("Model providers: subscription sign-in adapters (15-model-providers.md)")),
        PolicyRule.Choice(
            nameof(ModelProviders),
            policy => policy.ModelProviders,
            Enforcement.Later("Model providers: provider registration (15-model-providers.md)")),
        PolicyRule.Permission(
            nameof(OpenRegistration),
            policy => policy.OpenRegistration,
            Enforcement.Now("POST /api/register"),
            isLocked: false),
        PolicyRule.Requirement(
            nameof(RequireEmailVerification),
            policy => policy.RequireEmailVerification,
            Enforcement.Now("POST /api/register")),
        PolicyRule.Requirement(
            nameof(RequireTermsAcceptance),
            policy => policy.RequireTermsAcceptance,
            Enforcement.Now("POST /api/register")),
        PolicyRule.Requirement(
            nameof(RequireSetupCode),
            policy => policy.RequireSetupCode,
            Enforcement.Now("POST /api/setup")),
        PolicyRule.Choice(
            nameof(SpaceQuotas),
            policy => policy.SpaceQuotas,
            Enforcement.Later("Quotas, after Phase 0")),
        PolicyRule.Permission(
            nameof(SpaceMayDisableTaintedRunEscalation),
            policy => policy.SpaceMayDisableTaintedRunEscalation,
            Enforcement.Later("Phase 1: tainted-run escalation (06-mcp.md)")),
        PolicyRule.Permission(
            nameof(SpaceMayAutoLoadRemoteImages),
            policy => policy.SpaceMayAutoLoadRemoteImages,
            Enforcement.Later("Chat rendering of bot output")),
        PolicyRule.Choice(
            nameof(KeyRingProtection),
            policy => policy.KeyRingProtection,
            Enforcement.Now("Server start")),
        PolicyRule.Permission(
            nameof(AllowMigrationCredentialInServer),
            policy => policy.AllowMigrationCredentialInServer,
            Enforcement.Now("Server start")),
        PolicyRule.Choice(
            nameof(AuditRetention),
            policy => policy.AuditRetention,
            Enforcement.Later("Audit log retention and export")),
        PolicyRule.Permission(
            nameof(AllowDisablingDataRights),
            policy => policy.AllowDisablingDataRights,
            Enforcement.Later("Data export and account deletion endpoints")),
        PolicyRule.Permission(
            nameof(AllowTelemetryContent),
            policy => policy.AllowTelemetryContent,
            Enforcement.Later("Telemetry export")),
        PolicyRule.Permission(
            nameof(AllowOperatorContentAccess),
            policy => policy.AllowOperatorContentAccess,
            Enforcement.Later("Support access, if it is ever added")),
    ];

    #endregion

    #region Public Methods

    /// <summary>The rules whose value is looser than the preset allows. A rule the operator may set freely never counts.</summary>
    public IReadOnlyList<string> FindLoosenedRules(DeploymentPolicy preset) =>
    [
        .. Rules
            .Where(rule => rule.IsLocked && rule.Looseness(this) > rule.Looseness(preset))
            .Select(rule => rule.Name),
    ];

    /// <summary>
    /// Every reason this policy cannot run on this server, each naming its rule. Empty means the server may start.
    /// </summary>
    public IReadOnlyList<string> FindProblems(
        string presetName,
        DeploymentPolicy preset,
        string? termsVersion,
        ServerFacts facts)
    {
        List<string> problems =
        [
            .. Rules
                .Where(rule => !rule.HasDefinedValue(this))
                .Select(rule => $"{rule.Name} is not one of the values this rule has."),
            .. FindLoosenedRules(preset).Select(name => $"{name} is set looser than the '{presetName}' preset allows."),
        ];

        // Each requirement is checked when either the preset or the policy asks for the strict value, so no
        // configured value, however odd, switches a check off.
        if ((preset.KeyRingProtection is KeyRingProtection.CertificateOrKeyService
            || KeyRingProtection != KeyRingProtection.FilePermissions)
            && !facts.KeyRingProtectedByCertificate)
        {
            problems.Add(
                $"{nameof(KeyRingProtection)} requires a certificate: set DataProtection:CertificatePath and DataProtection:CertificateKeyPath.");
        }

        if ((!preset.AllowMigrationCredentialInServer || !AllowMigrationCredentialInServer) && facts.HoldsMigrationCredential)
        {
            problems.Add(
                $"{nameof(AllowMigrationCredentialInServer)} is off, so ConnectionStrings:DroverOwner must not be set on the serving process. Migrate in a separate job.");
        }

        if (OpenRegistration && (preset.RequireTermsAcceptance || RequireTermsAcceptance) && string.IsNullOrWhiteSpace(termsVersion))
        {
            problems.Add(
                $"{nameof(RequireTermsAcceptance)} is on with open registration, so Drover:Policy:TermsVersion must name the version people accept.");
        }

        return problems;
    }

    #endregion
}
