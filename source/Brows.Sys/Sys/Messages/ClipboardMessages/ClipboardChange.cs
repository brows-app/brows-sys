namespace Brows.Sys.Messages.ClipboardMessages;

/// <summary>
/// Signals that the system clipboard changed.
/// </summary>
/// <remarks>
/// This message does not contain clipboard contents. Consumers that need the contents must read them separately.
/// No initial clipboard snapshot is sent.
/// <see cref="SequenceNumber"/> is the current sequence number for the calling window station when this
/// notification is processed. It is a state hint, not an event identifier or a content snapshot. Zero means the
/// sequence number is unavailable, which can happen when clipboard access is limited. Delayed rendering affects
/// when the sequence number advances. Queued notifications can observe the same value, and the 32-bit value can
/// wrap rather than grow without bound.
/// </remarks>
public sealed record ClipboardChange : ClipboardMessage {
    /// <summary>
    /// Gets the clipboard sequence number observed while processing this notification.
    /// </summary>
    /// <value>
    /// The sequence number, or zero when it is unavailable.
    /// </value>
    public uint SequenceNumber { get; init; }
}
