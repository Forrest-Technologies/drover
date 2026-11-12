namespace Drover.Domain.Tests;

public sealed class DeploymentPolicyTests
{
    #region Private Fields

    private static readonly ServerFacts protectedServer = new(KeyRingProtectedByCertificate: true, HoldsMigrationCredential: false);

    #endregion

    #region Public Methods

    [Fact]
    public void Every_property_of_the_policy_has_exactly_one_rule_and_every_rule_names_a_property()
    {
        var properties = typeof(DeploymentPolicy)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name);

        DeploymentPolicy.Rules.Select(rule => rule.Name).Should().BeEquivalentTo(properties);
    }

    [Fact]
    public void The_hosted_preset_is_never_looser_than_the_self_hosted_one()
    {
        var looser = DeploymentPolicy.Rules
            .Where(rule => rule.Looseness(DeploymentPolicy.Hosted) > rule.Looseness(DeploymentPolicy.SelfHosted))
            .Select(rule => rule.Name);

        looser.Should().BeEmpty();
    }

    [Fact]
    public void The_hosted_preset_is_at_its_strictest_for_every_locked_rule_that_has_a_stricter_value()
    {
        var strictest = DeploymentPolicy.Rules
            .Where(rule => rule.IsLocked)
            .Where(rule => rule.Looseness(DeploymentPolicy.Hosted) > 0)
            .Select(rule => rule.Name);

        strictest.Should().BeEmpty();
    }

    [Fact]
    public void A_preset_is_found_by_its_name_and_an_unknown_name_is_reported()
    {
        DeploymentPolicy.TryGetPreset("hosted", out var hosted).Should().BeTrue();
        DeploymentPolicy.TryGetPreset("self_hosted", out var selfHosted).Should().BeTrue();
        DeploymentPolicy.TryGetPreset("Hosted ", out _).Should().BeFalse();
        DeploymentPolicy.TryGetPreset(null, out _).Should().BeFalse();

        hosted.Should().Be(DeploymentPolicy.Hosted);
        selfHosted.Should().Be(DeploymentPolicy.SelfHosted);
    }

    [Fact]
    public void Each_locked_rule_set_to_its_self_hosted_value_is_reported_as_loosened_under_hosted()
    {
        foreach (var rule in DeploymentPolicy.Rules.Where(rule => rule.IsLocked))
        {
            var property = typeof(DeploymentPolicy).GetProperty(rule.Name)!;
            var loosened = DeploymentPolicy.Hosted with { };
            property.SetValue(loosened, property.GetValue(DeploymentPolicy.SelfHosted));

            var found = loosened.FindLoosenedRules(DeploymentPolicy.Hosted);

            if (rule.Looseness(DeploymentPolicy.SelfHosted) > rule.Looseness(DeploymentPolicy.Hosted))
            {
                found.Should().Equal(rule.Name);
            }
            else
            {
                found.Should().BeEmpty();
            }
        }
    }

    [Fact]
    public void A_rule_tightened_beyond_the_preset_is_not_a_problem()
    {
        var tightened = DeploymentPolicy.SelfHosted with { AllowPrivateAddresses = false, SpaceQuotas = QuotaMode.SetByOperator };

        tightened.FindLoosenedRules(DeploymentPolicy.SelfHosted).Should().BeEmpty();
    }

    [Fact]
    public void A_limit_of_zero_or_less_is_not_a_value_the_rule_has()
    {
        var problems = (DeploymentPolicy.SelfHosted with { MaxModelListBytes = 0, MaxModelsPerConnection = -1 }).FindProblems(
            DeploymentPolicy.SelfHostedName,
            DeploymentPolicy.SelfHosted,
            null,
            new ServerFacts(KeyRingProtectedByCertificate: false, HoldsMigrationCredential: false));

        problems.Should().Contain(problem => problem.StartsWith(nameof(DeploymentPolicy.MaxModelListBytes), StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith(nameof(DeploymentPolicy.MaxModelsPerConnection), StringComparison.Ordinal));
    }

    [Fact]
    public void A_limit_looser_than_the_hosted_preset_is_reported_as_loosened()
    {
        var loosened = DeploymentPolicy.Hosted with { MaxModelListBytes = DeploymentPolicy.SelfHostedMaxModelListBytes };

        loosened.FindLoosenedRules(DeploymentPolicy.Hosted).Should().Equal(nameof(DeploymentPolicy.MaxModelListBytes));
        (DeploymentPolicy.Hosted with { MaxModelListBytes = 1024 }).FindLoosenedRules(DeploymentPolicy.Hosted).Should().BeEmpty();
    }

    [Fact]
    public void Open_registration_is_the_one_rule_an_operator_of_a_hosted_server_may_open()
    {
        var open = DeploymentPolicy.Hosted with { OpenRegistration = true };

        open.FindLoosenedRules(DeploymentPolicy.Hosted).Should().BeEmpty();
        DeploymentPolicy.Rules.Where(rule => !rule.IsLocked).Select(rule => rule.Name)
            .Should().Equal(nameof(DeploymentPolicy.OpenRegistration));
    }

    [Fact]
    public void The_hosted_preset_without_a_certificate_cannot_start_and_names_the_rule()
    {
        var problems = DeploymentPolicy.Hosted.FindProblems(
            DeploymentPolicy.HostedName,
            DeploymentPolicy.Hosted,
            termsVersion: null,
            new ServerFacts(KeyRingProtectedByCertificate: false, HoldsMigrationCredential: false));

        problems.Should().ContainSingle().Which.Should().Contain(nameof(DeploymentPolicy.KeyRingProtection));
    }

    [Fact]
    public void The_hosted_preset_with_a_migration_credential_in_the_server_cannot_start_and_names_the_rule()
    {
        var problems = DeploymentPolicy.Hosted.FindProblems(
            DeploymentPolicy.HostedName,
            DeploymentPolicy.Hosted,
            termsVersion: null,
            new ServerFacts(KeyRingProtectedByCertificate: true, HoldsMigrationCredential: true));

        problems.Should().ContainSingle().Which.Should().Contain(nameof(DeploymentPolicy.AllowMigrationCredentialInServer));
    }

    [Fact]
    public void Open_registration_that_asks_for_terms_needs_a_terms_version()
    {
        var open = DeploymentPolicy.Hosted with { OpenRegistration = true };

        open.FindProblems(DeploymentPolicy.HostedName, DeploymentPolicy.Hosted, termsVersion: " ", protectedServer)
            .Should().ContainSingle().Which.Should().Contain(nameof(DeploymentPolicy.RequireTermsAcceptance));
        open.FindProblems(DeploymentPolicy.HostedName, DeploymentPolicy.Hosted, termsVersion: "2026-11", protectedServer)
            .Should().BeEmpty();
    }

    [Fact]
    public void The_self_hosted_preset_starts_with_nothing_configured()
    {
        DeploymentPolicy.SelfHosted
            .FindProblems(
                DeploymentPolicy.SelfHostedName,
                DeploymentPolicy.SelfHosted,
                termsVersion: null,
                new ServerFacts(KeyRingProtectedByCertificate: false, HoldsMigrationCredential: true))
            .Should().BeEmpty();
    }

    #endregion
}
