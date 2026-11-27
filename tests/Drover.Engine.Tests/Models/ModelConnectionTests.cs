namespace Drover.Engine.Tests.Models;

/// <summary>
/// What a connection does when its operator switches the kind of its provider off: it stays readable, and the next
/// call that would use it is refused in the operator's words, without reaching the provider.
/// </summary>
public sealed class ModelConnectionTests(PostgresServer postgres)
{
    #region Public Methods

    [Fact]
    public async Task A_connection_of_a_kind_that_was_switched_off_fails_its_next_call_with_the_operators_sentence()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Never said.")]));
        var host = world.IdleHost(SwitchOff);

        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
        var service = scope.ServiceProvider.GetRequiredService<ModelConnectionService>();
        var listed = await ListConnections(scope.ServiceProvider, service);
        var test = await service.Test(world.ConnectionId, RunWorld.Token);
        var refresh = await service.RefreshModels(world.ConnectionId, RunWorld.Token);

        listed.Should().ContainSingle().Which.Id.Should().Be(world.ConnectionId);
        var sentence = $"The operator of this server switched {ScriptedModelProvider.ConnectionKind} off.";
        test!.Failure.Should().Be(ModelFailure.Rejected);
        test.Message.Should().Be(sentence);
        refresh!.Failure.Should().Be(ModelFailure.Rejected);
        refresh.Message.Should().Be(sentence);
        world.Model.KeysSeen.Should().BeEmpty("the provider is never reached");
    }

    [Fact]
    public async Task A_run_on_a_connection_of_a_kind_that_was_switched_off_fails_in_the_operators_words()
    {
        var script = new ScriptedModel([new TextOutput("Never said.")]);
        await using var world = await RunWorld.Create(postgres, script);
        world.StartHost(replaceServices: SwitchOff);
        var threadId = await world.NewThread();

        var runId = await world.Post(threadId, "Hello");
        var run = await world.WaitForStatus(runId, RunStatus.Failed, RunStatus.Completed);

        run.Status.Should().Be(RunStatus.Failed);
        run.Error!.Code.Should().Be("model_unavailable");
        run.Error.Message.Should().Be($"The operator of this server switched {ScriptedModelProvider.ConnectionKind} off.");
        script.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_kind_the_operator_lists_is_called_as_before()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Never said.")]));
        var host = world.IdleHost(services => Offer(services, ScriptedModelProvider.ConnectionKind));

        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
        var test = await scope.ServiceProvider.GetRequiredService<ModelConnectionService>().Test(world.ConnectionId, RunWorld.Token);

        test!.Result!.Succeeded.Should().BeTrue();
    }

    #endregion

    #region Private Methods

    private static async Task<List<ModelConnection>> ListConnections(IServiceProvider services, ModelConnectionService service)
    {
        await using var work = await services.GetRequiredService<TenantScope>().Begin(RunWorld.Token);
        var listed = await service.ListConnections(RunWorld.Token);
        await work.Commit(RunWorld.Token);
        return listed;
    }

    private static void SwitchOff(IServiceCollection services) => Offer(services);

    /// <summary>The deployment offers only the kinds listed: the operator's list.</summary>
    private static void Offer(IServiceCollection services, params string[] kinds) =>
        services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions
        {
            Rules = DeploymentPolicy.SelfHosted with { ModelProviders = ModelProviderSet.OperatorList, AllowedModelProviderKinds = kinds },
        }));

    #endregion
}
