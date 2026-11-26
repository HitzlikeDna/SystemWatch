using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SystemWatch.Native;

namespace SystemWatch.Monitoring;

internal static class ProcessKiller
{
    public const int Success = 0;
    public const int ErrorAccessDenied = 5;
    private const int ErrorInvalidParameter = 87; // Prozess existiert nicht mehr

    /// <summary>Prozesse, deren Beenden Windows sofort abstürzen ließe.</summary>
    private static readonly HashSet<string> CriticalImages = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "Secure System", "Memory Compression", "MemCompression",
        "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "services.exe", "lsass.exe", "LsaIso.exe",
    };

    public static bool IsCritical(string imageName) => CriticalImages.Contains(imageName);

    /// <summary>Beendet den Prozess. Gibt 0 oder den Win32-Fehlercode zurück.</summary>
    public static int Kill(ProcessIdentity process)
    {
        IntPtr handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessTerminate | NativeMethods.ProcessQueryLimitedInformation, false, process.Pid);

        if (handle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            return error == ErrorInvalidParameter ? Success : error;
        }

        try
        {
            // Die PID könnte inzwischen zu einem anderen Prozess gehören.
            if (NativeMethods.GetProcessTimes(handle, out long created, out _, out _, out _) && created != process.CreateTime)
                return Success;

            return NativeMethods.TerminateProcess(handle, 1) ? Success : Marshal.GetLastWin32Error();
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
