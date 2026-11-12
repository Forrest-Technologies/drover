namespace Drover.Data.Mappings;

/// <summary>Spells a C# name the way the database does: <c>PendingApproval</c> becomes <c>pending_approval</c>.</summary>
public static class SnakeCase
{
    #region Public Methods

    public static string Of(string name)
    {
        var spelled = new StringBuilder(name.Length + 4);

        foreach (var letter in name)
        {
            if (char.IsUpper(letter) && spelled.Length > 0)
            {
                spelled.Append('_');
            }

            spelled.Append(char.ToLowerInvariant(letter));
        }

        return spelled.ToString();
    }

    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum => Of(value.ToString());

    #endregion
}
