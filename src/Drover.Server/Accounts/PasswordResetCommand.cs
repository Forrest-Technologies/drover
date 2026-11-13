namespace Drover.Server.Accounts;

/// <summary>
/// The operator's way back into an account whose password is forgotten, on a server that cannot send email.
/// <c>--reset-password person@example.com</c> runs instead of the server, makes a one-time reset code for that
/// account, prints it to the log, and exits. The person enters it with a new password; it works once, within
/// fifteen minutes, and ends every session of theirs. The code is the only secret this prints, as with the setup code.
/// </summary>
public static class PasswordResetCommand
{
    #region Public Methods

    /// <summary>Connects as the server would, issues the code, and returns the process exit code.</summary>
    public static async Task<int> Run(IConfiguration configuration, string email)
    {
        var policy = new DeploymentPolicySetup(configuration).Resolve();
        var connectionString = DatabaseStartup.ApplicationConnection(configuration, policy.Rules.AllowMigrationCredentialInServer);
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddConsole());
        services.AddSingleton(TimeProvider.System);
        services.AddDroverData(connectionString);
        services.AddDroverEngine();
        await using var provider = services.BuildServiceProvider();
        return await Issue(provider, email);
    }

    public static async Task<int> Issue(IServiceProvider services, string email)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PasswordResetCommand).FullName!);
        var code = await scope.ServiceProvider.GetRequiredService<PasswordService>().IssueResetCode(email, CancellationToken.None);

        if (code is null)
        {
            logger.LogError("No account has the email {Email}. Nothing was changed.", email);
            return 1;
        }

        logger.LogWarning(
            "Password reset code for {Email}: {ResetCode}. It works once, within {Minutes} minutes, and ends every session of that account.",
            email,
            code,
            PasswordService.ResetCodeLifetime.TotalMinutes);
        return 0;
    }

    #endregion

    #region Constants

    public const string Argument = "--reset-password";

    #endregion
}
