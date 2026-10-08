namespace Brows.Sys.Messages.DeviceMessages;

/// <summary>
/// Specifies the kind of device represented by a device payload.
/// </summary>
public enum DeviceKind {
    /// <summary>
    /// No device kind is specified.
    /// </summary>
    None = 0,
    /// <summary>
    /// A logical volume.
    /// </summary>
    Volume = 1,
    /// <summary>
    /// A port or a device connected to a port.
    /// </summary>
    Port = 2,
    /// <summary>
    /// A device interface.
    /// </summary>
    Interface = 3
}
