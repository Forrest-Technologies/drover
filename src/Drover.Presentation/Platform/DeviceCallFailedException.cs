namespace Drover.Presentation.Platform;

/// <summary>
/// A call to the device that failed in a way a person can be told about: the clipboard refused, or the platform
/// would not say whether notifications are allowed. A platform's side throws it in place of its own exception
/// types, so a view model never has to know them.
/// </summary>
public sealed class DeviceCallFailedException : Exception
{
    #region Constructors

    public DeviceCallFailedException(string message)
        : base(message)
    {
    }

    public DeviceCallFailedException(string message, Exception inner)
        : base(message, inner)
    {
    }

    #endregion
}
