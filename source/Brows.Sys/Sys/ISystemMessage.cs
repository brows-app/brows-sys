namespace Brows.Sys;

/// <summary>
/// Represents a typed operating-system notification.
/// </summary>
public interface ISystemMessage {
    /// <summary>
    /// Gets the category of the notification.
    /// </summary>
    /// <value>
    /// The category used to classify this message.
    /// </value>
    SystemMessageKind SystemMessageKind { get; }
}
