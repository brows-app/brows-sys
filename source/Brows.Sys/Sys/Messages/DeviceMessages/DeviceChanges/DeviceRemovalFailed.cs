namespace Brows.Sys.Messages.DeviceMessages.DeviceChanges;

/// <summary>
/// Represents a notification that a device-removal request failed or was canceled.
/// </summary>
/// <remarks>
/// The supplied Windows backend uses this record when a previously requested removal is canceled.
/// </remarks>
public sealed record DeviceRemovalFailed : DeviceChange {
    /// <summary>
    /// Gets the change kind for a failed or canceled removal request.
    /// </summary>
    /// <value>
    /// <see cref="DeviceChangeKind.RemovalFailed"/>.
    /// </value>
    public sealed override DeviceChangeKind DeviceChangeKind =>
        DeviceChangeKind.RemovalFailed;
}
