namespace Drover.Server.Policy;

public static class PolicyServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Loads the deployment policy, checks it when the host starts, and gives every outbound HTTP client the
    /// handler that enforces the private-address rule. A client built with <c>new HttpClient()</c> would skip
    /// it, so outbound calls always come from <c>IHttpClientFactory</c>.
    /// </summary>
    public static IServiceCollection AddDroverPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        var setup = new DeploymentPolicySetup(configuration);
        services.AddOptions<DeploymentPolicyOptions>().ValidateOnStart();
        services.AddSingleton<IConfigureOptions<DeploymentPolicyOptions>>(setup);
        services.AddSingleton<IValidateOptions<DeploymentPolicyOptions>>(setup);
        services.AddScoped<PolicyService>();
        services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(provider =>
            OutboundHttp.CreateHandler(provider.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value.Rules)));
        return services;
    }

    #endregion
}
