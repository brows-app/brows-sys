namespace Brows.Sys;

/// <summary>
/// Specifies the category of a system notification.
/// </summary>
public enum SystemMessageKind {
    /// <summary>
    /// No system notification category is specified.
    /// </summary>
    None = 0,

    /// <summary>
    /// A notification concerning a device.
    /// </summary>
    Device = 1,

    /// <summary>
    /// A notification that the system clipboard changed.
    /// </summary>
    Clipboard = 2
}
