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
    public void The_hosted_preset_is_at_its_strictest_for_every_locked_rule_the_owner_has_not_decided_to_allow_there()
    {
        var loose = DeploymentPolicy.Rules
            .Where(rule => rule.IsLocked && !rule.IsAllowedWhenHosted)
            .Where(rule => rule.Looseness(DeploymentPolicy.Hosted) > 0)
            .Select(rule => rule.Name);

        loose.Should().BeEmpty();
    }

    [Fact]
    public void The_rules_allowed_when_hosted_are_exactly_the_owners_decisions_and_each_is_on_there_and_can_be_turned_off()
    {
        var allowed = DeploymentPolicy.Rules.Where(rule => rule.IsAllowedWhenHosted).ToList();

        allowed.Select(rule => rule.Name).Should().Equal(
            nameof(DeploymentPolicy.AllowBotsBuildingBots),
            nameof(DeploymentPolicy.AllowConversationSummaries));
        allowed.Select(rule => $"{rule.IsLocked} {rule.ReadText(DeploymentPolicy.Hosted)}").Should().AllBe("True true", "each is locked and on in the hosted preset");
        (DeploymentPolicy.Hosted with { AllowBotsBuildingBots = false, AllowConversationSummaries = false })
            .FindLoosenedRules(DeploymentPolicy.Hosted).Should().BeEmpty("an operator may always turn one off");
    }

    [Fact]
    public void A_rule_allowed_when_hosted_does_not_hide_any_other_rule_left_loose()
    {
        var accidentallyLoose = DeploymentPolicy.Hosted with { AllowDesktopSandboxes = true };

        accidentallyLoose.FindLoosenedRules(DeploymentPolicy.Hosted).Should().Equal(nameof(DeploymentPolicy.AllowDesktopSandboxes));
        DeploymentPolicy.Rules.Where(rule => rule.IsLocked && !rule.IsAllowedWhenHosted && rule.Looseness(accidentallyLoose) > 0)
            .Select(rule => rule.Name).Should().Equal(nameof(DeploymentPolicy.AllowDesktopSandboxes));
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
    public void A_self_hosted_operator_may_raise_any_limit_but_not_turn_on_a_locked_permission()
    {
        var raised = DeploymentPolicy.SelfHosted with
        {
            MaxDelegationDepth = 50,
            MaxDelegationTreeCostCents = 1_000_000,
            MaxTokensPerRun = 50_000_000,
        };

        raised.FindProblems(DeploymentPolicy.SelfHostedName, DeploymentPolicy.SelfHosted, null, new(false, false))
            .Should().BeEmpty("a limit on a self-hosted server is a safe default its operator may raise");
        raised.FindLoosenedRules(DeploymentPolicy.Hosted)
            .Should().Contain(nameof(DeploymentPolicy.MaxDelegationDepth), "the hosted preset still holds every limit");
        (DeploymentPolicy.SelfHosted with { AllowWrittenRulePatterns = true })
            .FindLoosenedRules(DeploymentPolicy.SelfHosted, limitsAreDefaults: true)
            .Should().Contain(nameof(DeploymentPolicy.AllowWrittenRulePatterns), "only limits rise; a locked permission stays locked");
    }

    [Fact]
    public void The_unmetered_call_bound_is_a_default_on_self_hosted_and_a_ceiling_on_hosted()
    {
        DeploymentPolicy.SelfHosted.MaxUnmeteredModelCallsPerRun.Should().Be(200);
        DeploymentPolicy.Hosted.MaxUnmeteredModelCallsPerRun.Should().Be(50);

        var raised = DeploymentPolicy.SelfHosted with { MaxUnmeteredModelCallsPerRun = 1000 };
        var unlimited = DeploymentPolicy.SelfHosted with { MaxUnmeteredModelCallsPerRun = int.MaxValue };

        raised.FindProblems(DeploymentPolicy.SelfHostedName, DeploymentPolicy.SelfHosted, null, new(false, false))
            .Should().BeEmpty("the operator may raise the safety bound on their own server");
        unlimited.FindProblems(DeploymentPolicy.SelfHostedName, DeploymentPolicy.SelfHosted, null, new(false, false))
            .Should().BeEmpty("the largest value stands for no limit");
        DeploymentPolicy.Rules.Single(rule => rule.Name == nameof(DeploymentPolicy.MaxUnmeteredModelCallsPerRun)).ReadText(unlimited)
            .Should().Be(PolicyRule.NoLimit);
        raised.FindLoosenedRules(DeploymentPolicy.Hosted)
            .Should().Contain(nameof(DeploymentPolicy.MaxUnmeteredModelCallsPerRun), "it is looser than the hosted preset");
        (DeploymentPolicy.Hosted with { MaxUnmeteredModelCallsPerRun = 51 })
            .FindProblems(DeploymentPolicy.HostedName, DeploymentPolicy.Hosted, null, new(false, false))
            .Should().Contain(problem => problem.StartsWith(nameof(DeploymentPolicy.MaxUnmeteredModelCallsPerRun), StringComparison.Ordinal));
    }

    [Fact]
    public void A_self_hosted_server_sets_no_limit_on_model_calls_or_on_the_runs_of_a_tree()
    {
        DeploymentPolicy.SelfHosted.MaxStepsPerRun.Should().Be(int.MaxValue);
        DeploymentPolicy.SelfHosted.MaxRunsPerDelegationTree.Should().Be(int.MaxValue);
        new DelegationLimits(5, int.MaxValue, 100).MaxHandoffs.Should().Be(int.MaxValue, "no limit has nothing to subtract from");
        new DelegationLimits(5, 50, 100).MaxHandoffs.Should().Be(49);
        DeploymentPolicy.Hosted.MaxStepsPerRun.Should().Be(50);
        DeploymentPolicy.Hosted.MaxRunsPerDelegationTree.Should().Be(50);
    }

    [Fact]
    public void Voice_is_unbounded_on_a_self_hosted_server_except_for_the_recording_it_holds_in_memory()
    {
        DeploymentPolicy.SelfHosted.MaxVoiceRecordingBytes.Should().Be(25 * 1024 * 1024);
        DeploymentPolicy.SelfHosted.MaxReadAloudCharacters.Should().Be(int.MaxValue);
        DeploymentPolicy.SelfHosted.MaxCallMinutes.Should().Be(int.MaxValue);
        DeploymentPolicy.Hosted.MaxVoiceRecordingBytes.Should().Be(10 * 1024 * 1024);
        DeploymentPolicy.Hosted.MaxReadAloudCharacters.Should().Be(8000);
        DeploymentPolicy.Hosted.MaxCallMinutes.Should().Be(30);
        (DeploymentPolicy.SelfHosted with { MaxVoiceRecordingBytes = 100 * 1024 * 1024 })
            .FindProblems(DeploymentPolicy.SelfHostedName, DeploymentPolicy.SelfHosted, null, new(false, false))
            .Should().BeEmpty("the recording limit is a default a self-hosted operator may raise");
        (DeploymentPolicy.Hosted with { MaxCallMinutes = 31 }).FindLoosenedRules(DeploymentPolicy.Hosted)
            .Should().Equal(nameof(DeploymentPolicy.MaxCallMinutes));
    }

    [Fact]
    public void A_limit_at_the_largest_number_reads_as_no_limit_and_any_other_as_its_number()
    {
        string Text(string name, DeploymentPolicy policy) => DeploymentPolicy.Rules.Single(rule => rule.Name == name).ReadText(policy);

        Text(nameof(DeploymentPolicy.MaxCallMinutes), DeploymentPolicy.SelfHosted).Should().Be(PolicyRule.NoLimit);
        Text(nameof(DeploymentPolicy.MaxReadAloudCharacters), DeploymentPolicy.SelfHosted).Should().Be("No limit");
        Text(nameof(DeploymentPolicy.MaxRunsPerDelegationTree), DeploymentPolicy.SelfHosted).Should().Be("No limit");
        Text(nameof(DeploymentPolicy.MaxStepsPerRun), DeploymentPolicy.SelfHosted).Should().Be("No limit");
        Text(nameof(DeploymentPolicy.MaxCallMinutes), DeploymentPolicy.Hosted).Should().Be("30");
        Text(nameof(DeploymentPolicy.MaxVoiceRecordingBytes), DeploymentPolicy.SelfHosted).Should().Be("26214400");
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

    [Fact]
    public void Open_registration_while_private_addresses_are_allowed_cannot_start_and_names_both_settings()
    {
        var open = DeploymentPolicy.SelfHosted with { OpenRegistration = true };
        var closedToPrivate = open with { AllowPrivateAddresses = false };

        open.FindProblems(DeploymentPolicy.SelfHostedName, DeploymentPolicy.SelfHosted, termsVersion: null, protectedServer)
            .Should().ContainSingle().Which.Should()
            .Contain($"Drover:Policy:Rules:{nameof(DeploymentPolicy.AllowPrivateAddresses)}")
            .And.Contain($"Drover:Policy:Rules:{nameof(DeploymentPolicy.OpenRegistration)}");
        closedToPrivate.FindProblems(DeploymentPolicy.SelfHostedName, DeploymentPolicy.SelfHosted, termsVersion: null, protectedServer)
            .Should().BeEmpty();
        DeploymentPolicy.SelfHosted.AllowPrivateAddresses.Should().BeTrue();
    }

    #endregion
}
