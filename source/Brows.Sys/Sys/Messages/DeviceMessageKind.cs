namespace Brows.Sys.Messages;

/// <summary>
/// Specifies the category of a device notification.
/// </summary>
public enum DeviceMessageKind {
    /// <summary>
    /// A notification describing a change involving a device.
    /// </summary>
    Change = 0,
    /// <summary>
    /// A notification requesting a refresh of the system device inventory.
    /// </summary>
    TreeChange = 1
}
