namespace Brows.Sys;

/// <summary>
/// Produces typed system notifications and releases its listener resources when disposed.
/// </summary>
/// <remarks>
/// Consumers that obtain a messenger directly are responsible for disposing it when listening ends.
/// Event delivery does not guarantee a particular thread or synchronization context.
/// </remarks>
internal interface ISystemMessenger : IDisposable {
    /// <summary>
    /// Occurs when a system notification is available.
    /// </summary>
    /// <remarks>
    /// Subscribers should avoid blocking the delivery thread and marshal UI updates to the appropriate context.
    /// </remarks>
    event SystemMessageEventHandler SystemMessaged;
}
