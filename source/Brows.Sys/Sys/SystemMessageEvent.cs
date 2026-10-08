namespace Brows.Sys;

/// <summary>
/// Handles a system notification raised by a messenger.
/// </summary>
/// <param name="source">
/// The messenger or other object that raised the notification.
/// </param>
/// <param name="e">
/// The event data containing the notification.
/// </param>
public delegate void SystemMessageEventHandler(object source, SystemMessageEventArgs e);

/// <summary>
/// Provides the message carried by a system notification event.
/// </summary>
public sealed class SystemMessageEventArgs : EventArgs {
    /// <summary>
    /// Gets the message supplied when these event arguments were created.
    /// </summary>
    /// <value>
    /// The message supplied to the constructor.
    /// </value>
    public ISystemMessage Message { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemMessageEventArgs"/> class.
    /// </summary>
    /// <param name="message">
    /// The message to expose through <see cref="Message"/>.
    /// </param>
    /// <remarks>
    /// The supplied value is stored unchanged, including when it is <see langword="null"/>.
    /// </remarks>
    public SystemMessageEventArgs(ISystemMessage message) {
        Message = message;
    }
}
