namespace Brows.Sys.Messages.DeviceMessages;

/// <summary>
/// Indicates that the system device tree changed and consumers should refresh their device inventory.
/// </summary>
/// <remarks>
/// The notification identifies no individual device, so its inherited Device payload is unavailable.
/// </remarks>
public sealed record DeviceTreeChange : DeviceMessage {
    /// <summary>
    /// Gets the device-tree notification category.
    /// </summary>
    public sealed override DeviceMessageKind DeviceMessageKind => DeviceMessageKind.TreeChange;
}
