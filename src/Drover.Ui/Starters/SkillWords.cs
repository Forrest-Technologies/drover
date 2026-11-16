namespace Drover.Ui.Starters;

/// <summary>A skill as it is being written: its name, the line a bot reads to decide whether to use it, and its steps.</summary>
public sealed record SkillDraft(string Name, string WhenToUse, string Steps)
{
    public static SkillDraft Empty { get; } = new(string.Empty, string.Empty, string.Empty);
}

/// <summary>
/// A skill as the library lists it. <see cref="ProposedBy"/> is the bot that offered it, when one did, and
/// <see cref="ApprovedBy"/> the person who read and saved it.
/// </summary>
public sealed record SkillItem(
    Guid Id,
    string Name,
    string WhenToUse,
    int BotCount,
    string? ProposedBy = null,
    string? ApprovedBy = null,
    DateTimeOffset? ApprovedOn = null);

/// <summary>Skills in words.</summary>
public static class SkillWords
{
    #region Public Methods

    /// <summary>What is missing before a skill can be saved, one sentence each. Empty when it can be.</summary>
    public static IReadOnlyList<string> Missing(SkillDraft draft)
    {
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            missing.Add("Give the skill a name.");
        }

        if (string.IsNullOrWhiteSpace(draft.WhenToUse))
        {
            missing.Add("Say when to use it. A bot reads that line to decide.");
        }

        if (string.IsNullOrWhiteSpace(draft.Steps))
        {
            missing.Add("Write the steps.");
        }

        return missing;
    }

    /// <summary>How many bots have a skill: "No bot has it yet", "1 bot has it", "3 bots have it".</summary>
    public static string GivenTo(int bots) => bots switch
    {
        0 => "No bot has it yet",
        1 => "1 bot has it",
        _ => $"{bots} bots have it",
    };

    /// <summary>Where a skill came from, when a bot offered it. Null for one a person wrote.</summary>
    public static string? Origin(SkillItem skill, string moment) => skill.ProposedBy is { Length: > 0 } bot
        ? $"Offered by {bot}, approved by {skill.ApprovedBy ?? "a person"} {moment}"
        : null;

    /// <summary>The Skills section's closed line in the bot editor: "No skills", "1 skill", "3 skills".</summary>
    public static string Summary(int enabled) => enabled is 0 ? "No skills" : Words.Count(enabled, "skill");

    #endregion
}
