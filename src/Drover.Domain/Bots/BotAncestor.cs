namespace Drover.Domain.Bots;

/// <summary>
/// One row of the closure table: <see cref="AncestorBotId"/> is <see cref="Distance"/> steps above
/// <see cref="DescendantBotId"/>. Every bot has a row to itself at distance 0.
/// </summary>
public sealed class BotAncestor : ISpaceOwned
{
    public Guid SpaceId { get; set; }

    public Guid AncestorBotId { get; set; }

    public Guid DescendantBotId { get; set; }

    public int Distance { get; set; }
}
