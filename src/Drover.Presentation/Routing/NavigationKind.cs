namespace Drover.Presentation.Routing;

/// <summary>How the app came to an address.</summary>
public enum NavigationKind
{
    /// <summary>A new step: the address before it can be gone back to.</summary>
    Push,

    /// <summary>In place of the address before it, which cannot be gone back to.</summary>
    Replace,

    /// <summary>A step back or forward through addresses already visited, made by the app or by the browser.</summary>
    Travel,
}
