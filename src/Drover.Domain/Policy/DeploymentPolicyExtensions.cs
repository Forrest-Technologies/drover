namespace Drover.Domain.Policy;

public static class DeploymentPolicyExtensions
{
    #region Public Methods

    /// <summary>
    /// The hosted preset: every rule at its strict value, except the permissions the owner decided stay on because
    /// other rules bound them (<see cref="PolicyRule.IsAllowedWhenHosted"/>). Registration stays off until the
    /// operator turns it on.
    /// </summary>
    public static DeploymentPolicy AsHosted(this DeploymentPolicy policy) =>
        policy
            .WithoutPrivateAddresses()
            .WithBoundedModelDiscovery()
            .WithBoundedToolServers()
            .WithoutToolConnections()
            .WithBoundedSandboxes()
            .WithBoundedSandboxSecrets()
            .WithBoundedBotsPerSpace()
            .WithBoundedSchedules()
            .WithBoundedFiles()
            .WithBoundedDelegation()
            .WithBoundedRuns()
            .WithOperatorApprovedProviders()
            .WithVerifiedRegistrationOnly()
            .WithQuotasFromTheOperator()
            .WithSpacesUnableToLoosen()
            .WithProtectedKeyRing()
            .WithoutMigrationCredential()
            .WithEnforcedAuditRetention()
            .WithGuaranteedDataRights()
            .WithServerStatusForTheOperatorOnly()
            .WithoutWrittenRulePatterns()
            .WithBoundedConversationSummaries()
            .WithoutJudgedRules()
            .WithBoundedMemory()
            .WithoutWebTools()
            .WithoutImageGeneration()
            .WithBoundedPushTests()
            .WithBoundedVoice();

    public static DeploymentPolicy WithBoundedVoice(this DeploymentPolicy policy) =>
        policy with
        {
            MaxVoiceRecordingBytes = DeploymentPolicy.HostedMaxVoiceRecordingBytes,
            MaxReadAloudCharacters = DeploymentPolicy.HostedMaxReadAloudCharacters,
            MaxCallMinutes = DeploymentPolicy.HostedMaxCallMinutes,
        };

    public static DeploymentPolicy WithBoundedPushTests(this DeploymentPolicy policy) =>
        policy with { MaxPushTestsPerMinute = DeploymentPolicy.HostedMaxPushTestsPerMinute };

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
            AllowDesktopSandboxes = false,
        };

    public static DeploymentPolicy WithBoundedDelegation(this DeploymentPolicy policy) =>
        policy with
        {
            MaxRunsPerDelegationTree = DeploymentPolicy.HostedMaxRunsPerDelegationTree,
            MaxDelegationTreeCostCents = DeploymentPolicy.HostedMaxDelegationTreeCostCents,
        };

    public static DeploymentPolicy WithBoundedRuns(this DeploymentPolicy policy) =>
        policy with
        {
            MaxStepsPerRun = DeploymentPolicy.HostedMaxStepsPerRun,
            MaxUnmeteredModelCallsPerRun = DeploymentPolicy.HostedMaxUnmeteredModelCallsPerRun,
            MaxTokensPerRun = DeploymentPolicy.HostedMaxTokensPerRun,
        };

    public static DeploymentPolicy WithBoundedBotsPerSpace(this DeploymentPolicy policy) =>
        policy with { MaxBotsPerSpace = DeploymentPolicy.HostedMaxBotsPerSpace };

    public static DeploymentPolicy WithBoundedSandboxSecrets(this DeploymentPolicy policy) =>
        policy with { MaxSandboxSecretsPerSpace = DeploymentPolicy.HostedMaxSandboxSecretsPerSpace };

    public static DeploymentPolicy WithBoundedSchedules(this DeploymentPolicy policy) =>
        policy with
        {
            MinScheduleIntervalMinutes = DeploymentPolicy.HostedMinScheduleIntervalMinutes,
            MaxSchedulesPerSpace = DeploymentPolicy.HostedMaxSchedulesPerSpace,
        };

    public static DeploymentPolicy WithBoundedFiles(this DeploymentPolicy policy) =>
        policy with
        {
            MaxFileBytes = DeploymentPolicy.HostedMaxFileBytes,
            MaxFilesPerMessage = DeploymentPolicy.HostedMaxFilesPerMessage,
            MaxSpaceFileMegabytes = DeploymentPolicy.HostedMaxSpaceFileMegabytes,
            MaxDocumentPages = DeploymentPolicy.HostedMaxDocumentPages,
        };

    /// <summary>Whether a set lets the server register a provider, by whether the provider is a hard isolation boundary.</summary>
    public static bool Allows(this SandboxAdapterSet set, bool hardBoundary) =>
        hardBoundary || set is SandboxAdapterSet.All;

    public static DeploymentPolicy WithOperatorApprovedProviders(this DeploymentPolicy policy) =>
        policy with
        {
            SubscriptionSignIn = SubscriptionSignInMode.OnlyWithOperatorApproval,
            ModelProviders = ModelProviderSet.OperatorList,
            DecisionModelKinds = ModelProviderSet.OperatorList,
        };

    public static DeploymentPolicy WithVerifiedRegistrationOnly(this DeploymentPolicy policy) =>
        policy with { RequireEmailVerification = true, RequireTermsAcceptance = true };

    public static DeploymentPolicy WithQuotasFromTheOperator(this DeploymentPolicy policy) =>
        policy with { SpaceQuotas = QuotaMode.SetByOperator };

    public static DeploymentPolicy WithSpacesUnableToLoosen(this DeploymentPolicy policy) =>
        policy with { SpaceMayAutoLoadRemoteImages = false };

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

    public static DeploymentPolicy WithBoundedConversationSummaries(this DeploymentPolicy policy) =>
        policy with { MaxConversationSummarySeconds = DeploymentPolicy.HostedMaxConversationSummarySeconds };

    public static DeploymentPolicy WithoutJudgedRules(this DeploymentPolicy policy) =>
        policy with
        {
            AllowJudgedRules = false,
            MaxJudgedRuleSeconds = DeploymentPolicy.HostedMaxJudgedRuleSeconds,
            MaxJudgedChecksPerRun = DeploymentPolicy.HostedMaxJudgedChecksPerRun,
        };

    public static DeploymentPolicy WithBoundedMemory(this DeploymentPolicy policy) =>
        policy with
        {
            MaxMemoryNoteCharacters = DeploymentPolicy.HostedMaxMemoryNoteCharacters,
            MaxMemoryCharactersPerPerson = DeploymentPolicy.HostedMaxMemoryCharactersPerPerson,
            AllowMemoryService = false,
        };

    public static DeploymentPolicy WithoutWebTools(this DeploymentPolicy policy) =>
        policy with
        {
            AllowWebTools = false,
            MaxWebPageBytes = DeploymentPolicy.HostedMaxWebPageBytes,
            MaxWebRequestSeconds = DeploymentPolicy.HostedMaxWebRequestSeconds,
        };

    public static DeploymentPolicy WithoutImageGeneration(this DeploymentPolicy policy) =>
        policy with { AllowImageGeneration = false };

    #endregion
}
