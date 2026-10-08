namespace Brows.Sys.Messages.DeviceMessages.DeviceChanges;

/// <summary>
/// Represents a notification that a device or piece of media has arrived.
/// </summary>
public sealed record DeviceArrival : DeviceChange {
    /// <summary>
    /// Gets the change kind for an arrival notification.
    /// </summary>
    /// <value>
    /// <see cref="DeviceChangeKind.Arrival"/>.
    /// </value>
    public sealed override DeviceChangeKind DeviceChangeKind =>
        DeviceChangeKind.Arrival;
}
