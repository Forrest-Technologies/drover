namespace Drover.Engine.Push;

/// <summary>What became of a request to put a device's new subscription in the place of its previous one.</summary>
public enum PushMove
{
    Moved,
    NotThePersons,
    NotASubscription,
}
