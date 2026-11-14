namespace Drover.Server.Tests;

/// <summary>One row per rule of <see cref="DeploymentPolicy"/>: its setting under each preset.</summary>
public sealed record PolicyMatrixRow(string Rule, string SelfHosted, string Hosted)
{
    public string For(string preset) => preset == DeploymentPolicy.HostedName ? Hosted : SelfHosted;
}

/// <summary>
/// Settings the server refuses to start with together, though each is allowed alone. <see cref="Names"/> are the
/// settings the refusal must name, so the operator knows what to change.
/// </summary>
public sealed record PolicyRefusalRow(string Preset, IReadOnlyDictionary<string, string> Rules, IReadOnlyList<string> Names);

/// <summary>
/// The table the matrix tests run from. Adding a property to <see cref="DeploymentPolicy"/> without a row here
/// fails <c>Every_rule_of_the_policy_has_a_row_in_the_matrix</c>, so no rule ships without a stated value for
/// both presets and a test that starts the server under each.
/// </summary>
public static class PolicyMatrix
{
    #region Properties

    public static IReadOnlyList<PolicyMatrixRow> Rows { get; } =
    [
        new(nameof(DeploymentPolicy.AllowPrivateAddresses), "true", "false"),
        new(nameof(DeploymentPolicy.MaxModelListBytes), "8388608", "2097152"),
        new(nameof(DeploymentPolicy.MaxModelsPerConnection), "5000", "1000"),
        new(nameof(DeploymentPolicy.AllowToolConnections), "true", "false"),
        new(nameof(DeploymentPolicy.MaxToolResponseBytes), "8388608", "2097152"),
        new(nameof(DeploymentPolicy.MaxToolsPerConnection), "500", "200"),
        new(nameof(DeploymentPolicy.MaxToolSignInRenewalsPerPass), "50", "20"),
        new(nameof(DeploymentPolicy.MaxToolSignInRenewalPassesPerHour), "12", "12"),
        new(nameof(DeploymentPolicy.MaxToolSignInRenewalLeadMinutes), "15", "15"),
        new(nameof(DeploymentPolicy.SandboxAdapters), "All", "HardBoundaryOnly"),
        new(nameof(DeploymentPolicy.AllowHostStdioMcp), "false", "false"),
        new(nameof(DeploymentPolicy.SubscriptionSignIn), "WhereProviderPermits", "OnlyWithOperatorApproval"),
        new(nameof(DeploymentPolicy.ModelProviders), "All", "OperatorList"),
        new(nameof(DeploymentPolicy.OpenRegistration), "false", "false"),
        new(nameof(DeploymentPolicy.RequireEmailVerification), "false", "true"),
        new(nameof(DeploymentPolicy.RequireTermsAcceptance), "false", "true"),
        new(nameof(DeploymentPolicy.RequireSetupCode), "true", "true"),
        new(nameof(DeploymentPolicy.SpaceQuotas), "Unlimited", "SetByOperator"),
        new(nameof(DeploymentPolicy.SpaceMayDisableTaintedRunEscalation), "true", "false"),
        new(nameof(DeploymentPolicy.SpaceMayAutoLoadRemoteImages), "true", "false"),
        new(nameof(DeploymentPolicy.KeyRingProtection), "FilePermissions", "CertificateOrKeyService"),
        new(nameof(DeploymentPolicy.AllowMigrationCredentialInServer), "true", "false"),
        new(nameof(DeploymentPolicy.AuditRetention), "OperatorChoice", "MinimumEnforced"),
        new(nameof(DeploymentPolicy.AllowDisablingDataRights), "true", "false"),
        new(nameof(DeploymentPolicy.AllowTelemetryContent), "false", "false"),
        new(nameof(DeploymentPolicy.AllowOperatorContentAccess), "false", "false"),
        new(nameof(DeploymentPolicy.SpaceAdminsSeeServerStatus), "true", "false"),
        new(nameof(DeploymentPolicy.AllowWrittenRulePatterns), "false", "false"),
    ];

    /// <summary>
    /// Combinations a server refuses at start. <c>PolicyStartupTests</c> starts the server with each and checks the
    /// refusal names every setting in the row.
    /// </summary>
    public static IReadOnlyList<PolicyRefusalRow> Refusals { get; } =
    [
        new(
            DeploymentPolicy.SelfHostedName,
            new Dictionary<string, string> { [nameof(DeploymentPolicy.OpenRegistration)] = "true" },
            [
                $"Drover:Policy:Rules:{nameof(DeploymentPolicy.OpenRegistration)}",
                $"Drover:Policy:Rules:{nameof(DeploymentPolicy.AllowPrivateAddresses)}",
            ]),
    ];

    #endregion

    #region Public Methods

    public static string Setting(string rule, string preset) =>
        Rows.Single(row => row.Rule == rule).For(preset);

    #endregion
}
