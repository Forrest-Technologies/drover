namespace Drover.Server.Tests.Support;

/// <summary>
/// Host configuration for tests that record SQL. A hosted loop uses a real timer, whatever the test clock says,
/// so its queries can land in a recording window. Classes that compare captured statements start without loops.
/// </summary>
public static class TestHosts
{
    #region Public Methods

    public static void WithoutHostedServices(IServiceCollection services)
    {
        List<ServiceDescriptor> loops = [.. services.Where(service => service.ServiceType == typeof(IHostedService))];

        foreach (var loop in loops)
        {
            services.Remove(loop);
        }
    }

    #endregion
}
