using Brows.Sys.Messages.DeviceMessages;

namespace Brows.Sys.Messages;

/// <summary>
/// Provides the base record for notifications concerning devices.
/// </summary>
public abstract record DeviceMessage : SystemMessage {
    /// <summary>
    /// Gets the category of the device notification.
    /// </summary>
    /// <value>
    /// The kind of device message represented by this record.
    /// </value>
    public abstract DeviceMessageKind DeviceMessageKind { get; }

    /// <summary>
    /// Gets the system-message category for device notifications.
    /// </summary>
    /// <value>
    /// <see cref="SystemMessageKind.Device"/>.
    /// </value>
    public sealed override SystemMessageKind SystemMessageKind => SystemMessageKind.Device;

    /// <summary>
    /// Gets or initializes the device associated with this notification.
    /// </summary>
    /// <value>
    /// The affected device, or <see langword="null"/> when corresponding device information is unavailable.
    /// </value>
    /// <remarks>
    /// Consumers should inspect the payload type before accessing device-specific properties.
    /// </remarks>
    public Device Device { get; init; }
}
