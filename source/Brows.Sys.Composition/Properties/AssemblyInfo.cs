[assembly: ComVisible(false)]
[assembly: InternalsVisibleTo("Brows.Sys.Composition.Tests")]
[assembly: InternalsVisibleTo("Brows.Sys.Win32.Windows")]
[assembly: InternalsVisibleTo("Brows.Sys.Win32.Windows.Sample")]
[assembly: InternalsVisibleTo("Brows.Sys.Win32.Windows.Tests")]

#if NETFRAMEWORK
#pragma warning disable IDE0161 // Convert to file-scoped namespace
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace System.Runtime.CompilerServices{
#pragma warning restore IDE0130 // Namespace does not match folder structure
#pragma warning restore IDE0161 // Convert to file-scoped namespace
    using ComponentModel;
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit {
    }
}
#endif
