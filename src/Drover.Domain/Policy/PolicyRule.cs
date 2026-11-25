namespace Drover.Domain.Policy;

/// <summary>
/// One named rule of <see cref="DeploymentPolicy"/>: how to read its value, how loose that value is, and where it
/// is enforced. A higher looseness is a less strict value. A locked rule cannot be loosened below its preset.
/// </summary>
public sealed record PolicyRule(
    string Name,
    Func<DeploymentPolicy, object> Read,
    Func<DeploymentPolicy, int> Looseness,
    Enforcement Enforcement,
    bool IsLocked = true,
    Func<DeploymentPolicy, bool>? IsDefined = null)
{
    #region Properties

    /// <summary>
    /// A permission the hosted preset leaves on by the owner's decision, because what it could cost a shared server
    /// is bounded by other rules. Every other locked rule is at its strictest in the hosted preset, so a rule left
    /// loose there by accident is still caught. An operator may still turn such a rule off.
    /// </summary>
    public bool IsAllowedWhenHosted { get; init; }

    /// <summary>A numeric limit or floor: how much of something is allowed, as opposed to whether it is.</summary>
    public bool IsLimit { get; init; }

    #endregion

    #region Public Methods

    /// <summary>A rule that allows something when true.</summary>
    public static PolicyRule Permission(
        string name,
        Func<DeploymentPolicy, bool> read,
        Enforcement enforcement,
        bool isLocked = true,
        bool allowedWhenHosted = false) =>
        new(name, policy => read(policy), policy => read(policy) ? 1 : 0, enforcement, isLocked)
        {
            IsAllowedWhenHosted = allowedWhenHosted,
        };

    /// <summary>A rule that demands something when true.</summary>
    public static PolicyRule Requirement(
        string name,
        Func<DeploymentPolicy, bool> read,
        Enforcement enforcement,
        bool isLocked = true) =>
        new(name, policy => read(policy), policy => read(policy) ? 0 : 1, enforcement, isLocked);

    /// <summary>A rule with several values, declared from strictest to loosest.</summary>
    public static PolicyRule Choice<TValue>(
        string name,
        Func<DeploymentPolicy, TValue> read,
        Enforcement enforcement,
        bool isLocked = true)
        where TValue : struct, Enum =>
        new(
            name,
            policy => read(policy),
            policy => Convert.ToInt32(read(policy)),
            enforcement,
            isLocked,
            policy => Enum.IsDefined(read(policy)));

    /// <summary>
    /// A numeric limit. A higher value is looser; <paramref name="strictest"/> is the value the hosted preset sets,
    /// so it counts as looseness zero. A limit of zero or less is not a value the rule has.
    /// </summary>
    public static PolicyRule Limit(
        string name,
        Func<DeploymentPolicy, int> read,
        int strictest,
        Enforcement enforcement,
        bool isLocked = true) =>
        new(name, policy => read(policy), policy => read(policy) - strictest, enforcement, isLocked, policy => read(policy) > 0) { IsLimit = true };

    /// <summary>
    /// A numeric floor. A lower value is looser; <paramref name="strictest"/> is the value the hosted preset sets, so it
    /// counts as looseness zero. A floor of zero or less is not a value the rule has.
    /// </summary>
    public static PolicyRule Floor(
        string name,
        Func<DeploymentPolicy, int> read,
        int strictest,
        Enforcement enforcement,
        bool isLocked = true) =>
        new(name, policy => read(policy), policy => strictest - read(policy), enforcement, isLocked, policy => read(policy) > 0) { IsLimit = true };

    /// <summary>
    /// False for a choice whose value is not one of the declared members, which configuration binding accepts for
    /// a number such as -1. Such a value is never read as stricter than the preset.
    /// </summary>
    public bool HasDefinedValue(DeploymentPolicy policy) => IsDefined?.Invoke(policy) ?? true;

    public string ReadText(DeploymentPolicy policy) =>
        Read(policy) switch
        {
            true => "true",
            false => "false",
            var value => value.ToString() ?? string.Empty,
        };

    #endregion
}
