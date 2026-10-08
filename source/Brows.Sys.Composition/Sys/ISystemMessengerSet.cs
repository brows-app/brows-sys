using Brows.Composition;
using System;
using System.Collections.Generic;

namespace Brows.Sys;

/// <summary>
/// Provides an asynchronous stream that combines notifications from available system messenger factories.
/// </summary>
public interface ISystemMessengerSet : IExport {
    /// <summary>
    /// Reads the system messages produced for the specified window.
    /// </summary>
    /// <param name="window">
    /// The platform-specific window object or native handle passed to the available factories.
    /// </param>
    /// <param name="cancellationToken">
    /// The token used to cancel factory initialization and message reading.
    /// </param>
    /// <returns>
    /// An asynchronous sequence of system messages. Enumeration completes when all factories finish
    /// without producing a messenger; otherwise, it reads until the consumer stops, cancels, or encounters an error.
    /// </returns>
    /// <remarks>
    /// Factory creation begins when the sequence is enumerated. Each messenger is subscribed as it becomes available.
    /// Messages are buffered, so consumers should read promptly to avoid accumulating unread notifications.
    /// Ending enumeration detaches handlers and disposes the messengers owned by that enumeration.
    /// Disposing the enumeration while a read is still outstanding is supported: the abandoned read completes
    /// with a cancellation error, the same cleanup runs, and repeated disposal has no effect.
    /// Successfully created late results are disposed after active cleanup completes.
    /// Closing a window does not automatically end this sequence.
    /// The host should cancel its reader when the window closes.
    /// Platform-specific thread requirements still apply to the supplied window and any UI updates.
    /// The stream carries notifications and does not provide a native-message response or veto mechanism.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Factory initialization or message reading is canceled.
    /// </exception>
    /// <exception cref="AggregateException">
    /// One or more foreground cleanup operations fail and no factory or cancellation exception
    /// is already being reported.
    /// </exception>
    IAsyncEnumerable<ISystemMessage> ReadAllSystemMessages(object window, CancellationToken cancellationToken);
}
