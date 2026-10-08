namespace Brows.Sys.Messages.DeviceMessages;

/// <summary>
/// Provides the base record for device information attached to a notification.
/// </summary>
public abstract record Device {
    /// <summary>
    /// Gets the kind of device described by this record.
    /// </summary>
    /// <value>
    /// The device kind used to classify this payload.
    /// </value>
    public abstract DeviceKind DeviceKind { get; }
}
