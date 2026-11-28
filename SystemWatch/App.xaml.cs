using System;
using System.Windows;
using System.Windows.Threading;
using SystemWatch.Infrastructure;

namespace SystemWatch;

public partial class App : Application
{
    private SingleInstance? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);

        // Erhöhter Hilfsprozess zum Ein-/Ausschalten des Task-Manager-Ersatzes: nur Registry, keine Oberfläche.
        if (e.Args.Length >= 2 &&
            string.Equals(e.Args[0], TaskManagerReplacement.HelperArgument, StringComparison.OrdinalIgnoreCase))
        {
            bool enable = string.Equals(e.Args[1], "on", StringComparison.OrdinalIgnoreCase);
            Shutdown(TaskManagerReplacement.Apply(enable));
            return;
        }

        // Läuft SystemWatch schon (z. B. Strg+Umschalt+Esc bei offenem Fenster), dieses Fenster nach vorn holen.
        _instance = new SingleInstance();
        if (!_instance.TryAcquire())
        {
            _instance = null;
            SingleInstance.ActivateRunningInstance();
            Shutdown(0);
            return;
        }

        var settings = AppSettings.Load();
        ThemeManager.Initialize(settings.Theme);

        var window = new MainWindow(settings);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeManager.Shutdown();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Ein Überwachungstool soll wegen eines einzelnen Fehlers nicht komplett abstürzen.
        MessageBox.Show(e.Exception.Message, "SystemWatch – unerwarteter Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
