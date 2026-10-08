namespace Brows.Sys.Messages.DeviceMessages;

/// <summary>
/// Specifies the stage or outcome of a device change.
/// </summary>
public enum DeviceChangeKind {
    /// <summary>
    /// A device or piece of media has arrived and is available.
    /// </summary>
    Arrival,
    /// <summary>
    /// Removal of a device or piece of media has been requested.
    /// </summary>
    RemovalRequest,
    /// <summary>
    /// A previously requested removal failed or was canceled.
    /// </summary>
    RemovalFailed,
    /// <summary>
    /// A device or piece of media is about to be removed.
    /// </summary>
    RemovalPending,
    /// <summary>
    /// A device or piece of media has been removed.
    /// </summary>
    RemovalComplete,
}
