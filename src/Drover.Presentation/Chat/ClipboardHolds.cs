namespace Drover.Ui.Chat;

/// <summary>What a device's clipboard holds when the person pastes into the message field.</summary>
public enum ClipboardHolds
{
    /// <summary>Nothing the field or the conversation can use.</summary>
    Nothing,

    /// <summary>Text, which is what was meant even when a picture of it is there too.</summary>
    Text,

    /// <summary>Files, or a picture the device hands over.</summary>
    Files,

    /// <summary>A picture the device lists but does not hand over.</summary>
    PictureNotHandedOver,
}
