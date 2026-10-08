namespace Brows.Sys.Messages.DeviceMessages.Devices;

/// <summary>
/// Identifies a port reported by the operating system.
/// </summary>
public sealed record PortDevice : Device {
    /// <summary>
    /// Gets the port device kind.
    /// </summary>
    public sealed override DeviceKind DeviceKind => DeviceKind.Port;

    /// <summary>
    /// Gets or initializes the friendly port or connected-device name supplied by the notification.
    /// </summary>
    public string PortName { get; init; }
}
