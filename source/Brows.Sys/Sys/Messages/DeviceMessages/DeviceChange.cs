namespace Brows.Sys.Messages.DeviceMessages;

/// <summary>
/// Provides the base record for changes in device availability or removal state.
/// </summary>
public abstract record DeviceChange : DeviceMessage {
    /// <summary>
    /// Gets the kind of device change described by this notification.
    /// </summary>
    /// <value>
    /// The stage or outcome of the device change.
    /// </value>
    public abstract DeviceChangeKind DeviceChangeKind { get; }

    /// <summary>
    /// Gets the device-message category for change notifications.
    /// </summary>
    /// <value>
    /// <see cref="DeviceMessageKind.Change"/>.
    /// </value>
    public sealed override DeviceMessageKind DeviceMessageKind =>
        DeviceMessageKind.Change;
}
