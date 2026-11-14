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
            HoldsMigrationCredential: DatabaseStartup.HeldInitializationSettings(configuration).Count > 0);
        var problems = options.Rules.FindProblems(options.Preset, preset, options.TermsVersion, facts);
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    #endregion

    #region Constants

    public const string PresetVariable = "DROVER_POLICY";

    private const string Section = "Drover:Policy";

    #endregion
}
