namespace Brows.Sys.Messages.DeviceMessages.DeviceChanges;

/// <summary>
/// Represents a notification that removal of a device or piece of media has completed.
/// </summary>
public sealed record DeviceRemovalComplete : DeviceChange {
    /// <summary>
    /// Gets the change kind for a completed removal.
    /// </summary>
    /// <value>
    /// <see cref="DeviceChangeKind.RemovalComplete"/>.
    /// </value>
    public sealed override DeviceChangeKind DeviceChangeKind =>
        DeviceChangeKind.RemovalComplete;
}
