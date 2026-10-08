namespace Brows.Sys.Messages;

/// <summary>
/// Provides the base record for notifications concerning the clipboard.
/// </summary>
public abstract record ClipboardMessage : SystemMessage {
    /// <summary>
    /// Gets the clipboard notification category.
    /// </summary>
    public sealed override SystemMessageKind SystemMessageKind => SystemMessageKind.Clipboard;
}
