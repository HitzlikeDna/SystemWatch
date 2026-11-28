using System;
using System.Threading;
using SystemWatch.Native;

namespace SystemWatch.Infrastructure;

/// <summary>
/// Sorgt dafür, dass SystemWatch nur einmal läuft – wie der Task-Manager. Ein zweiter Start
/// (Verknüpfung, Strg+Umschalt+Esc, Taskleiste) holt stattdessen das vorhandene Fenster nach vorn.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\SystemWatch.SingleInstance.6E18FD78";

    /// <summary>Nachricht, mit der ein zweiter Start das laufende Fenster aktiviert.</summary>
    public static readonly uint ActivateMessage = NativeMethods.RegisterWindowMessage("SystemWatch.Activate.6E18FD78");

    private Mutex? _mutex;

    /// <summary><c>true</c>, wenn dies die erste Instanz ist.</summary>
    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            if (createdNew)
                return true;

            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Die laufende Instanz hat Adminrechte – ihr Mutex ist für uns nicht zugänglich, existiert aber.
            return false;
        }
    }

    /// <summary>Bittet die laufende Instanz, ihr Fenster zu zeigen.</summary>
    public static void ActivateRunningInstance()
    {
        // Dieser Prozess wurde vom Benutzer gestartet und darf den Vordergrund weitergeben.
        NativeMethods.AllowSetForegroundWindow(NativeMethods.AsfwAny);
        NativeMethods.PostMessage(NativeMethods.HwndBroadcast, ActivateMessage, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_mutex == null)
            return;

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Nicht (mehr) im Besitz – egal.
        }

        _mutex.Dispose();
        _mutex = null;
    }
}
