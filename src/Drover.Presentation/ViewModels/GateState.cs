namespace Drover.Presentation.ViewModels;

/// <summary>What the session gate knows about who is signed in.</summary>
public enum GateState
{
    Checking,
    SignedIn,
    Unreachable,
}
