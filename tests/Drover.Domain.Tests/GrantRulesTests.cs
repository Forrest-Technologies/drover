namespace Drover.Domain.Tests;

/// <summary>
/// The grant rule in C#. <c>Drover.Data.Tests.EffectiveGrantTests</c> runs the same cases against the SQL function
/// <c>effective_grants</c>, so the two cannot drift apart.
/// </summary>
public sealed class GrantRulesTests
{
    #region Shared Cases

    [Theory]
    [MemberData(nameof(EffectiveGrantCases.Names), MemberType = typeof(EffectiveGrantCases))]
    public void The_effective_grant_is_the_one_the_shared_case_table_expects(string name)
    {
        var grantCase = EffectiveGrantCases.Find(name);

        var effective = GrantRules.Effective(grantCase.Entries(), grantCase.OwnClosureRow);

        effective.Should().Be(grantCase.ExpectedEffect());
    }

    #endregion

    #region Rule

    [Fact]
    public void The_effects_are_ordered_deny_below_ask_below_allow()
    {
        GrantRules.Min(GrantEffect.Allow, GrantEffect.Ask).Should().Be(GrantEffect.Ask);
        GrantRules.Min(GrantEffect.Ask, GrantEffect.Deny).Should().Be(GrantEffect.Deny);
        GrantRules.Min(GrantEffect.Deny, GrantEffect.Allow).Should().Be(GrantEffect.Deny);
        GrantRules.Min(GrantEffect.Allow, GrantEffect.Allow).Should().Be(GrantEffect.Allow);
    }

    [Fact]
    public void A_path_with_no_entries_gives_no_grant()
    {
        GrantRules.Effective([]).Should().BeNull();
    }

    [Theory]
    [InlineData(GrantEffect.Allow, GrantEffect.Ask)]
    [InlineData(GrantEffect.Ask, GrantEffect.Ask)]
    [InlineData(GrantEffect.Deny, GrantEffect.Deny)]
    public void A_grant_that_needs_review_counts_as_ask_and_never_as_more(GrantEffect effect, GrantEffect counted)
    {
        GrantRules.Counted(new GrantEntry(effect, ReviewState.NeedsReview)).Should().Be(counted);
        GrantRules.Counted(new GrantEntry(effect, ReviewState.Current)).Should().Be(effect);
    }

    #endregion
}
