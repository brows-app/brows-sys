using Brows.Composition;
using System.Threading.Tasks;

namespace Brows.Sys;

/// <summary>
/// Provides platform-specific system messengers through composition.
/// </summary>
internal interface ISystemMessengerFactory : IExport {
    /// <summary>
    /// Creates a system messenger for a window supported by this factory.
    /// </summary>
    /// <param name="window">
    /// The platform-specific window object or native handle accepted by the implementation.
    /// </param>
    /// <param name="cancellationToken">
    /// The token used to request cancellation of messenger creation.
    /// </param>
    /// <returns>
    /// A task whose result is a messenger for the window, or <see langword="null"/> if the factory
    /// cannot provide a messenger for that window.
    /// </returns>
    /// <remarks>
    /// A caller that obtains a messenger directly owns that result and should dispose it when listening ends.
    /// Window representations and thread requirements depend on the platform implementation.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Messenger creation is canceled.
    /// </exception>
    Task<ISystemMessenger> CreateSystemMessenger(object window, CancellationToken cancellationToken);
}
