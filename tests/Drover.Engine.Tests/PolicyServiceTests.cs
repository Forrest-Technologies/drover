namespace Drover.Engine.Tests;

/// <summary>
/// The effective rule for a space is the deployment's rule narrowed by the space's settings in
/// <c>spaces.settings</c>, read from a real database. A space can narrow and cannot loosen.
/// </summary>
public sealed class PolicyServiceTests(PostgresServer postgres) : IAsyncLifetime
{
    #region Private Fields

    private TestDatabase database = null!; // set first thing in InitializeAsync

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync() => database = await postgres.CreateDatabase();

    public async ValueTask DisposeAsync()
    {
        await database.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_space_with_no_settings_gets_the_deployment_rules()
    {
        var space = await SeedSpace(new SpaceSettings());

        var selfHosted = await Effective(DeploymentPolicy.SelfHosted, space);
        var hosted = await Effective(DeploymentPolicy.Hosted, space);

        selfHosted.Should().Be(new EffectivePolicy(AllowPrivateAddresses: true, TaintedRunEscalation: true, AutoLoadRemoteImages: false));
        hosted.Should().Be(new EffectivePolicy(AllowPrivateAddresses: false, TaintedRunEscalation: true, AutoLoadRemoteImages: false));
    }

    [Fact]
    public async Task A_space_can_forbid_private_addresses_that_the_deployment_allows()
    {
        var space = await SeedSpace(new SpaceSettings { AllowPrivateAddresses = false });

        var effective = await Effective(DeploymentPolicy.SelfHosted, space);

        effective.AllowPrivateAddresses.Should().BeFalse();
    }

    [Fact]
    public async Task A_space_cannot_allow_private_addresses_that_the_deployment_forbids()
    {
        var space = await SeedSpace(new SpaceSettings { AllowPrivateAddresses = true });

        var effective = await Effective(DeploymentPolicy.Hosted, space);

        effective.AllowPrivateAddresses.Should().BeFalse();
    }

    [Fact]
    public async Task A_space_can_turn_off_tainted_run_escalation_only_where_the_deployment_lets_it()
    {
        var space = await SeedSpace(new SpaceSettings { TaintedRunEscalation = false });

        var selfHosted = await Effective(DeploymentPolicy.SelfHosted, space);
        var hosted = await Effective(DeploymentPolicy.Hosted, space);

        selfHosted.TaintedRunEscalation.Should().BeFalse();
        hosted.TaintedRunEscalation.Should().BeTrue();
    }

    [Fact]
    public async Task A_space_can_turn_on_remote_images_only_where_the_deployment_lets_it()
    {
        var space = await SeedSpace(new SpaceSettings { AutoLoadRemoteImages = true });

        var selfHosted = await Effective(DeploymentPolicy.SelfHosted, space);
        var hosted = await Effective(DeploymentPolicy.Hosted, space);

        selfHosted.AutoLoadRemoteImages.Should().BeTrue();
        hosted.AutoLoadRemoteImages.Should().BeFalse();
    }

    [Fact]
    public async Task A_deployment_rule_tightened_by_the_operator_cannot_be_loosened_by_a_space()
    {
        var tightened = DeploymentPolicy.SelfHosted with { SpaceMayAutoLoadRemoteImages = false };
        var space = await SeedSpace(new SpaceSettings { AutoLoadRemoteImages = true });

        var effective = await Effective(tightened, space);

        effective.AutoLoadRemoteImages.Should().BeFalse();
    }

    #endregion

    #region Private Methods

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ServiceProvider Build(DeploymentPolicy policy)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroverData(database.AppConnectionString);
        services.AddDroverEngine();
        services.AddSingleton<IOptions<DeploymentPolicyOptions>>(
            Options.Create(new DeploymentPolicyOptions { Rules = policy }));
        services.AddScoped<PolicyService>();
        return services.BuildServiceProvider();
    }

    private async Task<(Guid SpaceId, Guid UserId)> SeedSpace(SpaceSettings settings)
    {
        var userId = Guid.CreateVersion7();
        var spaceId = Guid.CreateVersion7();
        await using var provider = Build(DeploymentPolicy.SelfHosted);
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        var email = $"{userId:N}@example.test";
        var created = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAsync(
            new User { Id = userId, UserName = email, Email = email, DisplayName = "Owner" },
            $"pw-{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}");
        created.Succeeded.Should().BeTrue();
        db.Spaces.Add(new Space
        {
            Id = spaceId,
            Kind = SpaceKind.Personal,
            Name = "Owner",
            Slug = $"personal-{spaceId:N}",
            Settings = settings,
        });
        db.Memberships.Add(new Membership { SpaceId = spaceId, UserId = userId, Role = MembershipRole.Owner });
        await work.Commit(Token);
        return (spaceId, userId);
    }

    private async Task<EffectivePolicy> Effective(DeploymentPolicy policy, (Guid SpaceId, Guid UserId) space)
    {
        await using var provider = Build(policy);
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(space.SpaceId, space.UserId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var effective = await scope.ServiceProvider.GetRequiredService<PolicyService>().GetEffective(space.SpaceId, Token);
        await work.Commit(Token);
        return effective;
    }

    #endregion
}
