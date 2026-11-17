namespace Drover.Domain.Sandboxes;

/// <summary>
/// A capability for one provider sandbox. It carries the per-sandbox access token, so it is never logged, stored in a
/// run event, or shown: the printed form leaves the token out.
/// </summary>
public sealed record SandboxHandle(string ProviderSandboxId, Guid SpaceId, Guid SharingGroupId, string AccessToken)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"ProviderSandboxId = {ProviderSandboxId}, SpaceId = {SpaceId}, SharingGroupId = {SharingGroupId}");
        return true;
    }
}
