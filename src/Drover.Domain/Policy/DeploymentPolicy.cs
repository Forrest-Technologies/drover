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

    public const int SelfHostedMaxSandboxCommandSeconds = 3600;

    public const int HostedMaxSandboxCommandSeconds = 600;

    public const int SelfHostedMaxSandboxFileBytes = 50 * 1024 * 1024;

    public const int HostedMaxSandboxFileBytes = 10 * 1024 * 1024;

    public const int SelfHostedMaxSandboxIdleMinutes = 60;

    public const int HostedMaxSandboxIdleMinutes = 15;

    public const int SelfHostedMaxConversationSummarySeconds = 120;

    public const int HostedMaxConversationSummarySeconds = 60;

    public const int SelfHostedMaxBotsPerSpace = 1000;

    public const int HostedMaxBotsPerSpace = 100;

    public const int SelfHostedMinScheduleIntervalMinutes = 1;

    public const int HostedMinScheduleIntervalMinutes = 15;

    public const int SelfHostedMaxSchedulesPerSpace = 1000;

    public const int HostedMaxSchedulesPerSpace = 100;

    public const int SelfHostedMaxSandboxSecretsPerSpace = 100;

    public const int HostedMaxSandboxSecretsPerSpace = 50;

    public const int SelfHostedMaxFileBytes = 25 * 1024 * 1024;

    public const int HostedMaxFileBytes = 10 * 1024 * 1024;

    public const int SelfHostedMaxFilesPerMessage = 10;

    public const int HostedMaxFilesPerMessage = 5;

    public const int SelfHostedMaxSpaceFileMegabytes = 10 * 1024;

    public const int HostedMaxSpaceFileMegabytes = 1024;

    public const int SelfHostedMaxDocumentPages = 500;

    public const int HostedMaxDocumentPages = 100;

    public const int DelegationDepth = 5;

    public const int RunsPerDelegationTree = 50;

    public const int SelfHostedMaxDelegationTreeCostCents = 10_000;

    public const int HostedMaxDelegationTreeCostCents = 1_000;

    public const int StepsPerRun = 50;

    public const int SelfHostedMaxTokensPerRun = 5_000_000;

    public const int HostedMaxTokensPerRun = 2_000_000;

    public const int SelfHostedMaxMemoryNoteCharacters = 1000;

    public const int HostedMaxMemoryNoteCharacters = 500;

    public const int SelfHostedMaxMemoryCharactersPerPerson = 20_000;

    public const int HostedMaxMemoryCharactersPerPerson = 8000;

    public const int SelfHostedMaxWebPageBytes = 4 * 1024 * 1024;

    public const int HostedMaxWebPageBytes = 1024 * 1024;

    public const int SelfHostedMaxWebRequestSeconds = 30;

    public const int HostedMaxWebRequestSeconds = 15;

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

    /// <summary>Which sandbox providers the server registers. A provider without a hard isolation boundary is left out when strict.</summary>
    public SandboxAdapterSet SandboxAdapters { get; init; } = SandboxAdapterSet.All;

    /// <summary>The most seconds one sandbox command runs before it is stopped, whatever the bot asks for.</summary>
    public int MaxSandboxCommandSeconds { get; init; } = SelfHostedMaxSandboxCommandSeconds;

    /// <summary>The most bytes of one file a bot reads from or writes to a sandbox.</summary>
    public int MaxSandboxFileBytes { get; init; } = SelfHostedMaxSandboxFileBytes;

    /// <summary>
    /// The most minutes a sandbox keeps running after its last use before the provider pauses it. It caps the bot's
    /// own setting: a running sandbox costs the operator money whether it is used or not.
    /// </summary>
    public int MaxSandboxIdleMinutes { get; init; } = SelfHostedMaxSandboxIdleMinutes;

    /// <summary>
    /// A bot's sandbox may reach the whole internet (private ranges are always denied). Off in the hosted preset: under
    /// the operator's provider account, what a stranger's sandbox does on the open internet is the operator's liability.
    /// </summary>
    public bool AllowOpenSandboxNetwork { get; init; } = true;

    /// <summary>
    /// A bot's sandbox may have a screen it sees and drives (computer use). Off in the hosted preset: a sandbox with a
    /// screen needs a far larger machine, and there are no per-space quotas to bound what that costs the operator.
    /// </summary>
    public bool AllowDesktopSandboxes { get; init; } = true;

    /// <summary>
    /// A bot granted the crew tools may make, change, pause, and remove bots beneath itself, inside the space's caps and
    /// its own build limits, acting with the authority of the person its run is for. On in both presets, by the
    /// owner's decision: what it could cost is bounded by <see cref="MaxBotsPerSpace"/> on every path that makes a bot,
    /// and by <c>crew.add</c> always waiting for a person, whatever its grant. An operator may still turn it off; off,
    /// no crew tool is granted to a new bot or offered to a model, and a call of one is refused in words naming this rule.
    /// </summary>
    public bool AllowBotsBuildingBots { get; init; } = true;

    /// <summary>
    /// The most bots a space may hold that have not been removed, however they are made: by a person at the top or
    /// beneath a parent, by duplicating, or by a bot. A space's own <c>max_bots</c> may only lower it. Each is a row on
    /// the operator's database and a bot that can be run, so a shared server bounds it more tightly.
    /// </summary>
    public int MaxBotsPerSpace { get; init; } = SelfHostedMaxBotsPerSpace;

    /// <summary>
    /// The fewest minutes allowed between two times a schedule is due. Every time a schedule fires is a run on the
    /// operator's server that nobody is watching, so a shared server spaces them out. A shorter one is refused when it
    /// is saved.
    /// </summary>
    public int MinScheduleIntervalMinutes { get; init; } = SelfHostedMinScheduleIntervalMinutes;

    /// <summary>The most schedules a space may hold, across all of its bots. Each is unattended work on the operator's server.</summary>
    public int MaxSchedulesPerSpace { get; init; } = SelfHostedMaxSchedulesPerSpace;

    /// <summary>The most sandbox secrets a space may hold. Each is a credential the operator's server keeps and hands out.</summary>
    public int MaxSandboxSecretsPerSpace { get; init; } = SelfHostedMaxSandboxSecretsPerSpace;

    /// <summary>The most bytes of one file a person attaches to a message. A larger one is refused, not cut.</summary>
    public int MaxFileBytes { get; init; } = SelfHostedMaxFileBytes;

    /// <summary>The most files one message carries.</summary>
    public int MaxFilesPerMessage { get; init; } = SelfHostedMaxFilesPerMessage;

    /// <summary>
    /// The most megabytes of files one space keeps on the operator's disk, counted across every member's files,
    /// private ones included. A file that would take the space past it is refused when it is uploaded.
    /// </summary>
    public int MaxSpaceFileMegabytes { get; init; } = SelfHostedMaxSpaceFileMegabytes;

    /// <summary>
    /// The most pages of a PDF document, slides of a presentation, or sheets of a workbook the server reads for a bot.
    /// The server reads an attached document itself, on its own processor, so this bounds what one file costs it.
    /// </summary>
    public int MaxDocumentPages { get; init; } = SelfHostedMaxDocumentPages;

    /// <summary>
    /// How many levels below the run a person started a chain of delegations may reach. A space may only lower it.
    /// Each level is another bot working, and a chain is where a runaway loop spends money.
    /// </summary>
    public int MaxDelegationDepth { get; init; } = DelegationDepth;

    /// <summary>The most runs one run tree may hold, the run a person started included. A space may only lower it.</summary>
    public int MaxRunsPerDelegationTree { get; init; } = RunsPerDelegationTree;

    /// <summary>
    /// What a run tree may have cost, in hundredths of the price list's currency, before no further delegation is
    /// begun in it. A space may only lower it. It bounds what a chain of bots spends of a person's model credit
    /// without a person seeing each step.
    /// </summary>
    public int MaxDelegationTreeCostCents { get; init; } = SelfHostedMaxDelegationTreeCostCents;

    /// <summary>
    /// The most model calls one run makes before it is stopped with <c>step_limit</c>. A space may only lower it, and
    /// a bot may lower it again. A model that keeps calling tools without finishing is stopped here.
    /// </summary>
    public int MaxStepsPerRun { get; init; } = StepsPerRun;

    /// <summary>
    /// The most tokens, read and written together, one run uses before it is stopped with <c>token_limit</c>. A space
    /// may only lower it, and a bot may lower it again. It bounds what one run spends whatever its model costs.
    /// </summary>
    public int MaxTokensPerRun { get; init; } = SelfHostedMaxTokensPerRun;

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

    /// <summary>
    /// Whether a bot rule may look for text with a pattern a person wrote. The expression engine guards against
    /// costly text and not against a costly pattern, and nothing can stop one match once it has begun, so whoever
    /// may save a rule could stop the whole server. Off in every preset until Drover has a matcher whose cost is
    /// bounded. Off, a rule with such a condition is refused when it is saved, and one saved earlier is never built
    /// or run: it counts as not checked and takes its effect. "Contains" and the built-in kinds stay.
    /// </summary>
    public bool AllowWrittenRulePatterns { get; init; }

    /// <summary>
    /// The server keeps a running summary of each person's main conversation with a bot, written by the bot's own
    /// model on the space's own connection after a run ends. On in both presets, by the owner's decision: a summary is
    /// due only once enough new messages lie beyond the last one, so there are never more summaries than the runs
    /// people start, each is bounded in what it reads and writes, and each call is cut off at
    /// <see cref="MaxConversationSummarySeconds"/>. An operator may still turn it off.
    /// </summary>
    public bool AllowConversationSummaries { get; init; } = true;

    /// <summary>
    /// The most seconds one summary's model call may take. The call holds one of the server's run workers after the
    /// run it follows has ended, so on a shared server it is bounded more tightly: a slow model must not keep other
    /// people's runs waiting.
    /// </summary>
    public int MaxConversationSummarySeconds { get; init; } = SelfHostedMaxConversationSummarySeconds;

    /// <summary>
    /// The most characters one note a bot keeps about a person may hold. A bot's own setting may only lower it. Notes
    /// are short on purpose: one fact each, few enough to read in full and to give to every run.
    /// </summary>
    public int MaxMemoryNoteCharacters { get; init; } = SelfHostedMaxMemoryNoteCharacters;

    /// <summary>
    /// The most characters a bot's notes about one person may hold together. A bot's own setting may only lower it.
    /// Every run of the bot for that person is given all of them, so this bounds what memory adds to each model call;
    /// on a shared server it also bounds the rows one person's bot can make.
    /// </summary>
    public int MaxMemoryCharactersPerPerson { get; init; } = SelfHostedMaxMemoryCharactersPerPerson;

    /// <summary>
    /// A bot granted the web tools may search the web and read a page by its address. Off in the hosted preset: every
    /// search and every page is a request from the operator's server, on the operator's address, made for a stranger.
    /// Off, neither tool is offered to a model and a call of one is refused in words naming this rule.
    /// </summary>
    public bool AllowWebTools { get; init; } = true;

    /// <summary>
    /// The most bytes Drover reads of one web page a bot asks for, or of one answer from the search service. A larger
    /// answer is refused for a search and cut for a page, and the bot is told which.
    /// </summary>
    public int MaxWebPageBytes { get; init; } = SelfHostedMaxWebPageBytes;

    /// <summary>The most seconds one search or one page read may take, redirects included, before it is given up.</summary>
    public int MaxWebRequestSeconds { get; init; } = SelfHostedMaxWebRequestSeconds;

    /// <summary>
    /// A bot granted the image tool may create an image through its model connection. Off in the hosted preset until
    /// the operator can set a per-space spending limit. Off, the tool is not offered and a call is refused.
    /// </summary>
    public bool AllowImageGeneration { get; init; } = true;

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
            Enforcement.Now("Sandbox provider registration, at server start")),
        PolicyRule.Limit(
            nameof(MaxSandboxCommandSeconds),
            policy => policy.MaxSandboxCommandSeconds,
            HostedMaxSandboxCommandSeconds,
            Enforcement.Now("SandboxBroker, on every sandbox command")),
        PolicyRule.Limit(
            nameof(MaxSandboxFileBytes),
            policy => policy.MaxSandboxFileBytes,
            HostedMaxSandboxFileBytes,
            Enforcement.Now("SandboxBroker, on every sandbox file read and write")),
        PolicyRule.Limit(
            nameof(MaxSandboxIdleMinutes),
            policy => policy.MaxSandboxIdleMinutes,
            HostedMaxSandboxIdleMinutes,
            Enforcement.Now("SandboxBroker, on every use of a sandbox, and BotService, when a bot is saved")),
        PolicyRule.Permission(
            nameof(AllowOpenSandboxNetwork),
            policy => policy.AllowOpenSandboxNetwork,
            Enforcement.Now("SandboxBroker, on every use of a sandbox, and BotService, when a bot is saved")),
        PolicyRule.Permission(
            nameof(AllowDesktopSandboxes),
            policy => policy.AllowDesktopSandboxes,
            Enforcement.Now("SandboxBroker, on every use of a sandbox; RunExecutor, which offers no computer tool; BotService, when a bot is saved; the bot editor's sandbox options")),
        PolicyRule.Permission(
            nameof(AllowBotsBuildingBots),
            policy => policy.AllowBotsBuildingBots,
            Enforcement.Now("RunExecutor, which offers no crew tool; CrewService, on every crew tool call; BotService, which grants no crew tool to a new bot"),
            allowedWhenHosted: true),
        PolicyRule.Limit(
            nameof(MaxBotsPerSpace),
            policy => policy.MaxBotsPerSpace,
            HostedMaxBotsPerSpace,
            Enforcement.Now("BotService, on every path that makes a bot: created, created beneath a parent, duplicated, or made by a bot")),
        PolicyRule.Floor(
            nameof(MinScheduleIntervalMinutes),
            policy => policy.MinScheduleIntervalMinutes,
            HostedMinScheduleIntervalMinutes,
            Enforcement.Now("ScheduleService, when a schedule is added or changed")),
        PolicyRule.Limit(
            nameof(MaxSchedulesPerSpace),
            policy => policy.MaxSchedulesPerSpace,
            HostedMaxSchedulesPerSpace,
            Enforcement.Now("ScheduleService, when a schedule is added")),
        PolicyRule.Limit(
            nameof(MaxSandboxSecretsPerSpace),
            policy => policy.MaxSandboxSecretsPerSpace,
            HostedMaxSandboxSecretsPerSpace,
            Enforcement.Now("SandboxSecretService, when a secret is added")),
        PolicyRule.Limit(
            nameof(MaxFileBytes),
            policy => policy.MaxFileBytes,
            HostedMaxFileBytes,
            Enforcement.Now("FileService, on every upload, and the upload endpoint's body limit")),
        PolicyRule.Limit(
            nameof(MaxFilesPerMessage),
            policy => policy.MaxFilesPerMessage,
            HostedMaxFilesPerMessage,
            Enforcement.Now("RunService, on every message sent with files")),
        PolicyRule.Limit(
            nameof(MaxSpaceFileMegabytes),
            policy => policy.MaxSpaceFileMegabytes,
            HostedMaxSpaceFileMegabytes,
            Enforcement.Now("FileService, on every upload, against the space's stored total")),
        PolicyRule.Limit(
            nameof(MaxDocumentPages),
            policy => policy.MaxDocumentPages,
            HostedMaxDocumentPages,
            Enforcement.Now("FileService on upload and RunFiles on every run, when a document's text is read")),
        PolicyRule.Limit(
            nameof(MaxDelegationDepth),
            policy => policy.MaxDelegationDepth,
            DelegationDepth,
            Enforcement.Now("RunGovernor, when a bot delegates")),
        PolicyRule.Limit(
            nameof(MaxRunsPerDelegationTree),
            policy => policy.MaxRunsPerDelegationTree,
            RunsPerDelegationTree,
            Enforcement.Now("RunGovernor, when a bot delegates")),
        PolicyRule.Limit(
            nameof(MaxDelegationTreeCostCents),
            policy => policy.MaxDelegationTreeCostCents,
            HostedMaxDelegationTreeCostCents,
            Enforcement.Now("RunGovernor, when a bot delegates")),
        PolicyRule.Limit(
            nameof(MaxStepsPerRun),
            policy => policy.MaxStepsPerRun,
            StepsPerRun,
            Enforcement.Now("RunSession, before every model call of a run")),
        PolicyRule.Limit(
            nameof(MaxTokensPerRun),
            policy => policy.MaxTokensPerRun,
            HostedMaxTokensPerRun,
            Enforcement.Now("RunSession, before every model call of a run")),
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
        PolicyRule.Permission(
            nameof(AllowWrittenRulePatterns),
            policy => policy.AllowWrittenRulePatterns,
            Enforcement.Now("RuleService, when a rule is saved, tried, or read for a run")),
        PolicyRule.Permission(
            nameof(AllowConversationSummaries),
            policy => policy.AllowConversationSummaries,
            Enforcement.Now("ConversationSummarizer, after each run on a main thread"),
            allowedWhenHosted: true),
        PolicyRule.Limit(
            nameof(MaxConversationSummarySeconds),
            policy => policy.MaxConversationSummarySeconds,
            HostedMaxConversationSummarySeconds,
            Enforcement.Now("ConversationSummarizer, on every summary's model call")),
        PolicyRule.Limit(
            nameof(MaxMemoryNoteCharacters),
            policy => policy.MaxMemoryNoteCharacters,
            HostedMaxMemoryNoteCharacters,
            Enforcement.Now("MemoryService, on every note saved or changed; BotService, when a bot is saved")),
        PolicyRule.Limit(
            nameof(MaxMemoryCharactersPerPerson),
            policy => policy.MaxMemoryCharactersPerPerson,
            HostedMaxMemoryCharactersPerPerson,
            Enforcement.Now("MemoryService, on every note saved or changed and on every recall; BotService, when a bot is saved")),
        PolicyRule.Permission(
            nameof(AllowWebTools),
            policy => policy.AllowWebTools,
            Enforcement.Now("RunExecutor, which offers no web tool, and every web.search and web.fetch call")),
        PolicyRule.Limit(
            nameof(MaxWebPageBytes),
            policy => policy.MaxWebPageBytes,
            HostedMaxWebPageBytes,
            Enforcement.Now("Every web.fetch page and every web.search answer")),
        PolicyRule.Limit(
            nameof(MaxWebRequestSeconds),
            policy => policy.MaxWebRequestSeconds,
            HostedMaxWebRequestSeconds,
            Enforcement.Now("Every web.fetch and web.search call")),
        PolicyRule.Permission(
            nameof(AllowImageGeneration),
            policy => policy.AllowImageGeneration,
            Enforcement.Now("RunExecutor, which offers no image.create tool, and every image.create call")),
    ];

    #endregion

}
