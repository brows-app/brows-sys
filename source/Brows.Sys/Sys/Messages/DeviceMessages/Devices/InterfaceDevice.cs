namespace Brows.Sys.Messages.DeviceMessages.Devices;

/// <summary>
/// Identifies a device interface reported by the operating system.
/// </summary>
public sealed record InterfaceDevice : Device {
    /// <summary>
    /// Gets the interface device kind.
    /// </summary>
    public sealed override DeviceKind DeviceKind => DeviceKind.Interface;

    /// <summary>
    /// Gets or initializes the interface class identifier.
    /// </summary>
    public Guid InterfaceClassGuid { get; init; }

    /// <summary>
    /// Gets or initializes the native interface name copied from the notification.
    /// </summary>
    /// <remarks>
    /// This identifier is not a friendly display name or a resolved storage location.
    /// </remarks>
    public string InterfaceName { get; init; }
}
