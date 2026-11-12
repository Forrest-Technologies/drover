namespace Drover.Domain.Policy;

/// <summary>Where a rule is enforced. A rule that is not enforced yet names the work that will enforce it.</summary>
public sealed record Enforcement(bool IsEnforced, string Where)
{
    public static Enforcement Now(string where) => new(true, where);

    public static Enforcement Later(string where) => new(false, where);
}
