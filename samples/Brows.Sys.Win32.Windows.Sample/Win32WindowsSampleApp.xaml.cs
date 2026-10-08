using Brows.Composition;
using Brows.Sys;
using System.Windows;

namespace Brows;

internal sealed partial class Win32WindowsSampleApp : Application, IImportEnvironment {
    internal static ImportInfo CreateImportInfo() {
        return ImportInfo.Listed(
            variables: () => new(),
            list: () => [
                new SystemMessengerSet(),
                new Win32WindowsMessengerFactory(),
                new Win32WindowsSampleWindow()
            ]);
    }

    protected sealed override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        try {
            var imported = await Imports.Init(this, default);
            try {
                MainWindow = imported.Find<Win32WindowsSampleWindow>() ??
                    throw new InvalidOperationException("The sample window was not imported.");
                MainWindow.ShowDialog();
            }
            finally {
                imported.Kill();
            }
            Shutdown();
        }
        catch (Exception exception) {
            MessageBox.Show(exception.Message, "Brows.Sys sample startup failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    ImportInfo IImportEnvironment.ImportInfo => CreateImportInfo();
}
