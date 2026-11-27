namespace Drover.Server.Tests;

/// <summary>A model provider's client never follows a redirect, so a key header never goes to a host the answer names.</summary>
public sealed class ModelRedirectTests
{
    #region Public Methods

    [Fact]
    public void Every_model_client_is_built_without_automatic_redirects()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroverPolicy(new ConfigurationBuilder().Build());
        services.AddDroverModels(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        foreach (var name in new[] { OpenAICompatibleProvider.HttpClientName, OpenAICompatibleProvider.RealtimeHttpClientName, AnthropicProvider.HttpClientName, OpenAIProvider.HttpClientName })
        {
            Sockets(factory.CreateHandler(name)).AllowAutoRedirect.Should().BeFalse($"the client {name} must not follow a redirect");
        }
    }

    #endregion

    #region Private Methods

    private static SocketsHttpHandler Sockets(HttpMessageHandler handler)
    {
        var current = handler;

        while (current is DelegatingHandler delegating && delegating.InnerHandler is not null)
        {
            current = delegating.InnerHandler;
        }

        var invoker = (HttpMessageInvoker)current.GetType().GetField("guardedInvoker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(current)!;
        return (SocketsHttpHandler)typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(invoker)!;
    }

    #endregion
}
