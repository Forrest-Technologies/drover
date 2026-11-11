namespace Drover.Domain.Models;

/// <summary>Whether one credential serves the whole space or each person signs in themself.</summary>
public enum CredentialScope
{
    Space,
    PerUser,
}
