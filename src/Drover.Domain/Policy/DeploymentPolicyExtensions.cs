namespace Drover.Domain.Policy;

public static class DeploymentPolicyExtensions
{
    #region Public Methods

    /// <summary>The hosted preset: every rule at its strict value. Registration stays off until the operator turns it on.</summary>
    public static DeploymentPolicy AsHosted(this DeploymentPolicy policy) =>
        policy
            .WithoutPrivateAddresses()
            .WithBoundedModelDiscovery()
            .WithBoundedToolServers()
            .WithoutToolConnections()
            .WithBoundedSandboxes()
            .WithOperatorApprovedProviders()
            .WithVerifiedRegistrationOnly()
            .WithQuotasFromTheOperator()
            .WithSpacesUnableToLoosen()
            .WithProtectedKeyRing()
            .WithoutMigrationCredential()
            .WithEnforcedAuditRetention()
            .WithGuaranteedDataRights()
            .WithServerStatusForTheOperatorOnly()
            .WithoutWrittenRulePatterns();

    public static DeploymentPolicy WithoutPrivateAddresses(this DeploymentPolicy policy) =>
        policy with { AllowPrivateAddresses = false };

    public static DeploymentPolicy WithoutToolConnections(this DeploymentPolicy policy) =>
        policy with { AllowToolConnections = false };

    public static DeploymentPolicy WithBoundedToolServers(this DeploymentPolicy policy) =>
        policy with
        {
            MaxToolResponseBytes = DeploymentPolicy.HostedMaxToolResponseBytes,
            MaxToolsPerConnection = DeploymentPolicy.HostedMaxToolsPerConnection,
            MaxToolSignInRenewalsPerPass = DeploymentPolicy.HostedMaxToolSignInRenewalsPerPass,
        };

    public static DeploymentPolicy WithBoundedModelDiscovery(this DeploymentPolicy policy) =>
        policy with
        {
            MaxModelListBytes = DeploymentPolicy.HostedMaxModelListBytes,
            MaxModelsPerConnection = DeploymentPolicy.HostedMaxModelsPerConnection,
        };

    public static DeploymentPolicy WithBoundedSandboxes(this DeploymentPolicy policy) =>
        policy with
        {
            SandboxAdapters = SandboxAdapterSet.HardBoundaryOnly,
            MaxSandboxCommandSeconds = DeploymentPolicy.HostedMaxSandboxCommandSeconds,
            MaxSandboxFileBytes = DeploymentPolicy.HostedMaxSandboxFileBytes,
            MaxSandboxIdleMinutes = DeploymentPolicy.HostedMaxSandboxIdleMinutes,
            AllowOpenSandboxNetwork = false,
        };

    /// <summary>Whether a set lets the server register a provider, by whether the provider is a hard isolation boundary.</summary>
    public static bool Allows(this SandboxAdapterSet set, bool hardBoundary) =>
        hardBoundary || set is SandboxAdapterSet.All;

    public static DeploymentPolicy WithOperatorApprovedProviders(this DeploymentPolicy policy) =>
        policy with
        {
            SubscriptionSignIn = SubscriptionSignInMode.OnlyWithOperatorApproval,
            ModelProviders = ModelProviderSet.OperatorList,
        };

    public static DeploymentPolicy WithVerifiedRegistrationOnly(this DeploymentPolicy policy) =>
        policy with { RequireEmailVerification = true, RequireTermsAcceptance = true };

    public static DeploymentPolicy WithQuotasFromTheOperator(this DeploymentPolicy policy) =>
        policy with { SpaceQuotas = QuotaMode.SetByOperator };

    public static DeploymentPolicy WithSpacesUnableToLoosen(this DeploymentPolicy policy) =>
        policy with { SpaceMayDisableTaintedRunEscalation = false, SpaceMayAutoLoadRemoteImages = false };

    public static DeploymentPolicy WithProtectedKeyRing(this DeploymentPolicy policy) =>
        policy with { KeyRingProtection = KeyRingProtection.CertificateOrKeyService };

    public static DeploymentPolicy WithoutMigrationCredential(this DeploymentPolicy policy) =>
        policy with { AllowMigrationCredentialInServer = false };

    public static DeploymentPolicy WithEnforcedAuditRetention(this DeploymentPolicy policy) =>
        policy with { AuditRetention = AuditRetentionMode.MinimumEnforced };

    public static DeploymentPolicy WithGuaranteedDataRights(this DeploymentPolicy policy) =>
        policy with { AllowDisablingDataRights = false };

    public static DeploymentPolicy WithServerStatusForTheOperatorOnly(this DeploymentPolicy policy) =>
        policy with { SpaceAdminsSeeServerStatus = false };

    public static DeploymentPolicy WithoutWrittenRulePatterns(this DeploymentPolicy policy) =>
        policy with { AllowWrittenRulePatterns = false };

    #endregion
}
