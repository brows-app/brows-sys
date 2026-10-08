namespace Brows.Sys.Messages.DeviceMessages.DeviceChanges;

/// <summary>
/// Represents a notification that removal of a device or piece of media has been requested.
/// </summary>
/// <remarks>
/// This record describes a notification and does not provide a native-query response or veto mechanism.
/// </remarks>
public sealed record DeviceRemovalRequest : DeviceChange {
    /// <summary>
    /// Gets the change kind for a removal request.
    /// </summary>
    /// <value>
    /// <see cref="DeviceChangeKind.RemovalRequest"/>.
    /// </value>
    public sealed override DeviceChangeKind DeviceChangeKind =>
        DeviceChangeKind.RemovalRequest;
}
