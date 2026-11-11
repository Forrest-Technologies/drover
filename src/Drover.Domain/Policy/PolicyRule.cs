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
    #region Public Methods

    /// <summary>A rule that allows something when true.</summary>
    public static PolicyRule Permission(
        string name,
        Func<DeploymentPolicy, bool> read,
        Enforcement enforcement,
        bool isLocked = true) =>
        new(name, policy => read(policy), policy => read(policy) ? 1 : 0, enforcement, isLocked);

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
