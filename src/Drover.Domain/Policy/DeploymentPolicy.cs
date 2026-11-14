namespace Drover.Domain.Policy;

/// <summary>
/// Everything that differs between a server for one household and a service for strangers, as one typed value
/// per rule. Feature code asks one rule a specific question and never asks which preset is selected. A property
/// without an entry in <see cref="Rules"/> and a row in the matrix test fails the build.
/// </summary>
public sealed record DeploymentPolicy
{
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
                $"{nameof(AllowMigrationCredentialInServer)} is off, so the serving process must not be given ConnectionStrings:DroverOwner, Database:Password, Database:PasswordFile, Database:OwnerPassword or Database:OwnerPasswordFile. Give them only to the one-off --initialize-database run.");
        }

        if (OpenRegistration && (preset.RequireTermsAcceptance || RequireTermsAcceptance) && string.IsNullOrWhiteSpace(termsVersion))
        {
            problems.Add(
                $"{nameof(RequireTermsAcceptance)} is on with open registration, so Drover:Policy:TermsVersion must name the version people accept.");
        }

        // Strangers who register get a space of their own and can add connections, so they must not be able to aim the
        // server at its private network. A self-hoster who opens registration keeps local models by naming them
        // elsewhere or by giving up open registration; the server does not guess which.
        if (OpenRegistration && AllowPrivateAddresses)
        {
            problems.Add(
                $"{nameof(OpenRegistration)} is on while {nameof(AllowPrivateAddresses)} is on, so anyone who registers could aim a connection at this server's private network. Set Drover:Policy:Rules:{nameof(AllowPrivateAddresses)} to false, or set Drover:Policy:Rules:{nameof(OpenRegistration)} to false.");
        }

        return problems;
    }

    #endregion

    #region Constants

    public const string SelfHostedName = "self_hosted";

    public const string HostedName = "hosted";

    public const int SelfHostedMaxModelListBytes = 8 * 1024 * 1024;

    public const int HostedMaxModelListBytes = 2 * 1024 * 1024;

    public const int SelfHostedMaxModelsPerConnection = 5000;

    public const int HostedMaxModelsPerConnection = 1000;

    public const int SelfHostedMaxToolResponseBytes = 8 * 1024 * 1024;

    public const int HostedMaxToolResponseBytes = 2 * 1024 * 1024;

    public const int SelfHostedMaxToolsPerConnection = 500;

    public const int HostedMaxToolsPerConnection = 200;

    public const int SelfHostedMaxToolSignInRenewalsPerPass = 50;

    public const int HostedMaxToolSignInRenewalsPerPass = 20;

    public const int ToolSignInRenewalPassesEachHour = 12;

    public const int ToolSignInRenewalLeadMinutes = 15;

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

    /// <summary>The most bytes of a provider's model list Drover reads.</summary>
    public int MaxModelListBytes { get; init; } = SelfHostedMaxModelListBytes;

    /// <summary>The most models Drover accepts from one provider connection.</summary>
    public int MaxModelsPerConnection { get; init; } = SelfHostedMaxModelsPerConnection;

    /// <summary>
    /// Connections to remote tool servers. Off, none can be added, and none that exists is listed, signed in to,
    /// renewed, or called: a switch an operator can pull without removing anything. Off in the hosted preset until
    /// the work a shared server needs is done (renewal fairness, sign-ins across instances, revoking at the
    /// provider); a self-hoster decides for their own server.
    /// </summary>
    public bool AllowToolConnections { get; init; } = true;

    /// <summary>The most bytes of one answer from a tool server Drover reads: a tool list page, or a tool's result.</summary>
    public int MaxToolResponseBytes { get; init; } = SelfHostedMaxToolResponseBytes;

    /// <summary>The most tools Drover accepts from one tool connection.</summary>
    public int MaxToolsPerConnection { get; init; } = SelfHostedMaxToolsPerConnection;

    /// <summary>
    /// The most tool server sign-ins one background pass tries to renew. Each is a request to somebody else's
    /// server made on this server's own initiative, so the number is bounded.
    /// </summary>
    public int MaxToolSignInRenewalsPerPass { get; init; } = SelfHostedMaxToolSignInRenewalsPerPass;

    /// <summary>How many times an hour the server looks for tool server sign-ins that are running out.</summary>
    public int MaxToolSignInRenewalPassesPerHour { get; init; } = ToolSignInRenewalPassesEachHour;

    /// <summary>How many minutes before an access token runs out it is renewed in the background. More means renewing more often.</summary>
    public int MaxToolSignInRenewalLeadMinutes { get; init; } = ToolSignInRenewalLeadMinutes;

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

    /// <summary>
    /// The owners and admins of a space see the server's own rows on the status page: database role, migrations,
    /// key ring, public address, and the run queue of every space as counts. On a server for one household they are
    /// the operator; on a shared service they are strangers, and those rows are the operator's alone.
    /// </summary>
    public bool SpaceAdminsSeeServerStatus { get; init; } = true;

    #endregion

    #region Rule Table

    /// <summary>Every rule, in the order the status listing shows them.</summary>
    public static IReadOnlyList<PolicyRule> Rules { get; } =
    [
        PolicyRule.Permission(
            nameof(AllowPrivateAddresses),
            policy => policy.AllowPrivateAddresses,
            Enforcement.Now("OutboundHttp, on every outbound connection")),
        PolicyRule.Limit(
            nameof(MaxModelListBytes),
            policy => policy.MaxModelListBytes,
            HostedMaxModelListBytes,
            Enforcement.Now("Model discovery, in each provider adapter")),
        PolicyRule.Limit(
            nameof(MaxModelsPerConnection),
            policy => policy.MaxModelsPerConnection,
            HostedMaxModelsPerConnection,
            Enforcement.Now("Model discovery, in ModelConnectionService")),
        PolicyRule.Permission(
            nameof(AllowToolConnections),
            policy => policy.AllowToolConnections,
            Enforcement.Now("ToolConnectionService, wherever a tool server is added or reached")),

        PolicyRule.Limit(
            nameof(MaxToolResponseBytes),
            policy => policy.MaxToolResponseBytes,
            HostedMaxToolResponseBytes,
            Enforcement.Now("Every answer from a tool server, in the tool source adapter")),
        PolicyRule.Limit(
            nameof(MaxToolsPerConnection),
            policy => policy.MaxToolsPerConnection,
            HostedMaxToolsPerConnection,
            Enforcement.Now("Tool listing, in the tool source adapter and the tool catalog")),
        PolicyRule.Limit(
            nameof(MaxToolSignInRenewalsPerPass),
            policy => policy.MaxToolSignInRenewalsPerPass,
            HostedMaxToolSignInRenewalsPerPass,
            Enforcement.Now("Background renewal of tool server sign-ins, in ToolSignInRenewal")),
        PolicyRule.Limit(
            nameof(MaxToolSignInRenewalPassesPerHour),
            policy => policy.MaxToolSignInRenewalPassesPerHour,
            ToolSignInRenewalPassesEachHour,
            Enforcement.Now("Background renewal of tool server sign-ins, in ToolSignInRenewer")),
        PolicyRule.Limit(
            nameof(MaxToolSignInRenewalLeadMinutes),
            policy => policy.MaxToolSignInRenewalLeadMinutes,
            ToolSignInRenewalLeadMinutes,
            Enforcement.Now("Background renewal of tool server sign-ins, in ToolSignInRenewal")),
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
        PolicyRule.Permission(
            nameof(SpaceAdminsSeeServerStatus),
            policy => policy.SpaceAdminsSeeServerStatus,
            Enforcement.Now("GET /api/spaces/{spaceId}/status")),
    ];

    #endregion

}
