using System.Windows;
using System.Windows.Threading;

namespace SystemWatch;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Ein Überwachungstool soll wegen eines einzelnen Fehlers nicht komplett abstürzen.
        MessageBox.Show(e.Exception.Message, "SystemWatch – unerwarteter Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
