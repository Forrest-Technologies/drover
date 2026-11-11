namespace Drover.Domain.Tenancy;

public static class Audience
{
    /// <summary>The all-zero id that means every member of the space. Matches <c>sys.everyone()</c>.</summary>
    public static readonly Guid Everyone = Guid.Empty;
}
