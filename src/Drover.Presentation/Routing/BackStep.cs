namespace Drover.Presentation.Routing;

/// <summary>What one step back did.</summary>
public enum BackStep
{
    /// <summary>Something open over the screen closed, and the app stayed where it was.</summary>
    Closed,

    /// <summary>The app went to the address before.</summary>
    WentBack,

    /// <summary>The screen holds work, so it stayed and asked the person first.</summary>
    Held,

    /// <summary>Nothing was open and there was nowhere to go back to, so the step leaves the app.</summary>
    Out,
}
