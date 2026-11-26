namespace Drover.Contracts.Themes;

/// <summary>
/// The names a client gives the tokens of a resolved theme. The native client names a token by dropping the
/// "--dv-" prefix, capitalizing each part and joining: "bg-0" is <c>DvBg0</c>, "radius-control" is
/// <c>DvRadiusControl</c>. The six steps of the type scale keep the name of the pixel size they have in Night Yard,
/// so <c>DvText12</c> is the first step of any theme, the smallest, and <c>DvText14</c> its base. The line height of a
/// step is named the same way: <c>DvLine14</c> is the base step's size times the text leading.
/// </summary>
public static class TokenNames
{
    #region Private Fields

    private static readonly int[] stepNames = [12, 13, 14, 16, 20, 28];

    #endregion

    #region Public Methods

    /// <summary>The native name of a color or number token: "bg-0" is "DvBg0", "size-2" is "DvText14".</summary>
    public static string Of(string token) => token.Split('-') switch
    {
        ["size", var step] when int.TryParse(step, out var index) => $"DvText{stepNames[index]}",
        ["line", var step] when int.TryParse(step, out var index) => $"DvLine{stepNames[index]}",
        var parts => $"Dv{string.Concat(parts.Select(part => $"{char.ToUpperInvariant(part[0])}{part[1..]}"))}",
    };

    #endregion
}
