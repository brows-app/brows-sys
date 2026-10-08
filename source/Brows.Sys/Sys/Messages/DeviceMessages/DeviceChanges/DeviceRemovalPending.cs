namespace Brows.Sys.Messages.DeviceMessages.DeviceChanges;

/// <summary>
/// Represents a notification that a device or piece of media is about to be removed.
/// </summary>
public sealed record DeviceRemovalPending : DeviceChange {
    /// <summary>
    /// Gets the change kind for a pending removal.
    /// </summary>
    /// <value>
    /// <see cref="DeviceChangeKind.RemovalPending"/>.
    /// </value>
    public sealed override DeviceChangeKind DeviceChangeKind =>
        DeviceChangeKind.RemovalPending;
}
