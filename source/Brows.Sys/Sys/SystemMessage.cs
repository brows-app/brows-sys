namespace Brows.Sys;

/// <summary>
/// Provides the base record for typed system notifications.
/// </summary>
public abstract record SystemMessage : ISystemMessage {
    /// <summary>
    /// Gets the category of the notification.
    /// </summary>
    /// <value>
    /// The category represented by this message.
    /// </value>
    public abstract SystemMessageKind SystemMessageKind { get; }
}
