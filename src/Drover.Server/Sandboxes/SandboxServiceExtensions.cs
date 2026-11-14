namespace Drover.Server.Sandboxes;

public static class SandboxServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the sandbox provider and the sandbox tools when the operator has configured a provider key, and
    /// nothing when not: a server without a key has no sandboxes, which is not an error. The key is read from
    /// <c>Drover:Sandbox:E2B:ApiKey</c>, or from the file <c>Drover:Sandbox:E2B:ApiKeyFile</c> names (a container
    /// secret), and held only in the adapter's options in memory: never in the database, the vault, a response, or
    /// the log. A key that is plainly not an E2B key stops the server, naming the setting and never the value. A
    /// provider the deployment policy's <see cref="DeploymentPolicy.SandboxAdapters"/> leaves out is not registered.
    /// </summary>
    public static IServiceCollection AddDroverSandboxes(this IServiceCollection services, IConfiguration configuration, DeploymentPolicy policy)
    {
        if (ReadKey(configuration) is not { } key)
        {
            return services;
        }

        // E2B runs each sandbox in its own virtual machine, a hard isolation boundary.
        if (!policy.SandboxAdapters.Allows(hardBoundary: true))
        {
            return services;
        }

        var section = configuration.GetSection(Section);
        services.AddE2BSandboxProvider(options =>
        {
            options.ApiKey = key;
            options.Domain = section["Domain"] is { Length: > 0 } domain ? domain : options.Domain;
            options.Template = section["Template"] is { Length: > 0 } template ? template : options.Template;
            options.DesktopTemplate = section["DesktopTemplate"] is { Length: > 0 } desktop ? desktop : options.DesktopTemplate;
        });
        services.AddSandboxTools();
        return services;
    }

    #endregion

    #region Private Methods

    private static string? ReadKey(IConfiguration configuration)
    {
        var value = configuration[KeySetting];
        var path = configuration[KeyFileSetting];

        var key = (string.IsNullOrWhiteSpace(value), string.IsNullOrWhiteSpace(path)) switch
        {
            (true, true) => null,
            (false, false) => throw new InvalidOperationException(
                $"Both {KeySetting} and {KeyFileSetting} are set. Give the sandbox provider key one way only."),
            (false, true) => value!.Trim(),
            (true, false) => ReadKeyFile(path!),
        };

        return key is null || E2BOptions.IsWellFormedKey(key)
            ? key
            : throw new InvalidOperationException(
                $"{(string.IsNullOrWhiteSpace(value) ? KeyFileSetting : KeySetting)} does not hold an E2B API key, which starts with e2b_. Correct the setting, or remove it to run without sandboxes.");
    }

    private static string ReadKeyFile(string path) =>
        File.Exists(path)
            ? File.ReadAllText(path).Trim()
            : throw new InvalidOperationException($"{KeyFileSetting} must name a readable file holding the sandbox provider key.");

    #endregion

    #region Constants

    public const string KeySetting = "Drover:Sandbox:E2B:ApiKey";

    public const string KeyFileSetting = "Drover:Sandbox:E2B:ApiKeyFile";

    private const string Section = "Drover:Sandbox:E2B";

    #endregion
}
