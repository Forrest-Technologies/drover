namespace Drover.Server.Policy;

/// <summary>
/// Builds <see cref="DeploymentPolicyOptions"/> from configuration and checks them once, at start. The preset comes
/// from <c>DROVER_POLICY</c> (or <c>Drover:Policy:Preset</c>); <c>Drover:Policy:Rules:*</c> tightens single rules; a
/// rule set looser than the preset, a key that names no rule, or a rule the server cannot meet stops the server and
/// names the rule.
/// </summary>
public sealed class DeploymentPolicySetup(IConfiguration configuration)
    : IConfigureOptions<DeploymentPolicyOptions>, IValidateOptions<DeploymentPolicyOptions>
{
    #region Private Fields

    private static readonly string[] initializationOnlySettings =
    [
        DatabaseStartup.AdminPassword,
        $"{DatabaseStartup.AdminPassword}{DatabaseStartup.FileSuffix}",
        DatabaseStartup.OwnerPassword,
        $"{DatabaseStartup.OwnerPassword}{DatabaseStartup.FileSuffix}",
    ];

    #endregion

    #region Public Methods

    public DeploymentPolicyOptions Resolve()
    {
        var options = new DeploymentPolicyOptions();
        Configure(options);
        var validation = Validate(Options.DefaultName, options);
        if (validation.Failed)
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(DeploymentPolicyOptions), validation.Failures);
        }

        return options;
    }

    #endregion

    #region Interface Implementations

    public void Configure(DeploymentPolicyOptions options)
    {
        var section = configuration.GetSection(Section);
        options.Preset = configuration[PresetVariable] ?? section["Preset"] ?? DeploymentPolicy.SelfHostedName;
        options.TermsVersion = section["TermsVersion"];

        DeploymentPolicy.TryGetPreset(options.Preset, out var preset);
        var rules = preset with { };
        section.GetSection("Rules").Bind(rules, binder => binder.ErrorOnUnknownConfiguration = true);
        options.Rules = rules;
    }

    public ValidateOptionsResult Validate(string? name, DeploymentPolicyOptions options)
    {
        if (!DeploymentPolicy.TryGetPreset(options.Preset, out var preset))
        {
            return ValidateOptionsResult.Fail(
                $"{PresetVariable} is '{options.Preset}'. It must be '{DeploymentPolicy.SelfHostedName}' or '{DeploymentPolicy.HostedName}'.");
        }

        var facts = new ServerFacts(
            KeyRingProtectedByCertificate: !string.IsNullOrWhiteSpace(configuration["DataProtection:CertificatePath"]),
            HoldsMigrationCredential: HoldsMigrationCredential());
        var problems = options.Rules.FindProblems(options.Preset, preset, options.TermsVersion, facts);
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// The owner connection, or a password only the one-off initialization needs, given to this process in either
    /// form. Whether it is set is all that is read; the value is never looked at.
    /// </summary>
    private bool HoldsMigrationCredential() =>
        !string.IsNullOrWhiteSpace(configuration.GetConnectionString("DroverOwner"))
        || initializationOnlySettings.Any(setting => !string.IsNullOrEmpty(configuration[setting]));

    #endregion

    #region Constants

    public const string PresetVariable = "DROVER_POLICY";

    private const string Section = "Drover:Policy";

    #endregion
}
