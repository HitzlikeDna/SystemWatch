using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SystemWatch.Infrastructure;

public enum ReplacementState
{
    /// <summary>Windows startet den normalen Task-Manager.</summary>
    Off,

    /// <summary>Windows startet stattdessen genau diese SystemWatch.exe.</summary>
    Active,

    /// <summary>Eintrag zeigt auf eine SystemWatch.exe an einem anderen Ort (verschoben/alte Kopie).</summary>
    OtherLocation,

    /// <summary>Ein anderes Programm (z. B. Process Explorer) ersetzt den Task-Manager.</summary>
    OtherProgram,
}

public enum ReplacementChange
{
    Success,
    Cancelled,
    Failed,
}

public readonly struct ReplacementStatus(ReplacementState state, string? path)
{
    public ReplacementState State { get; } = state;

    /// <summary>Eingetragenes Programm (ohne Argumente).</summary>
    public string? Path { get; } = path;

    /// <summary>Der eingetragene Pfad existiert nicht – der Task-Manager lässt sich dann gar nicht öffnen.</summary>
    public bool IsBroken => State is ReplacementState.OtherLocation or ReplacementState.OtherProgram &&
                            !string.IsNullOrEmpty(Path) && !File.Exists(Path);
}

/// <summary>
/// Ersetzt den Windows-Task-Manager über „Image File Execution Options“ – derselbe Weg, den
/// Process Explorer nutzt: Startet irgendetwas taskmgr.exe (Strg+Umschalt+Esc, Taskleiste,
/// Strg+Alt+Entf), startet Windows stattdessen den eingetragenen „Debugger“, also SystemWatch.
/// Der Schlüssel liegt unter HKLM; Ändern braucht deshalb Administratorrechte.
/// </summary>
internal static class TaskManagerReplacement
{
    /// <summary>Startargument für den erhöhten Hilfsprozess: <c>--replace-taskmgr on|off</c>.</summary>
    public const string HelperArgument = "--replace-taskmgr";

    /// <summary>Kennzeichnet einen Start anstelle des Task-Managers.</summary>
    public const string LaunchedArgument = "--as-taskmgr";

    private const string IfeoKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe";
    private const string DebuggerValue = "Debugger";
    private const int ErrorCancelled = 1223;

    public static string ExecutablePath { get; } = GetExecutablePath();

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public static ReplacementStatus Query()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(IfeoKeyPath);
            if (key?.GetValue(DebuggerValue) is not string { Length: > 0 } debugger)
                return new ReplacementStatus(ReplacementState.Off, null);

            string path = ExtractExecutable(debugger);
            if (SamePath(path, ExecutablePath))
                return new ReplacementStatus(ReplacementState.Active, path);
            return new ReplacementStatus(IsSystemWatch(path) ? ReplacementState.OtherLocation : ReplacementState.OtherProgram, path);
        }
        catch
        {
            return new ReplacementStatus(ReplacementState.Off, null);
        }
    }

    /// <summary>
    /// Schaltet den Ersatz um. Ohne Adminrechte startet SystemWatch sich dafür kurz selbst
    /// mit erhöhten Rechten (Windows zeigt die Benutzerkontensteuerung).
    /// </summary>
    public static async Task<ReplacementChange> SetAsync(bool enable)
    {
        if (IsElevated)
            return Apply(enable) == 0 ? ReplacementChange.Success : ReplacementChange.Failed;

        var start = new ProcessStartInfo(ExecutablePath, $"{HelperArgument} {(enable ? "on" : "off")}")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(start);
            if (process == null)
                return ReplacementChange.Failed;

            bool exited = await Task.Run(() => process.WaitForExit(30_000)).ConfigureAwait(true);
            return exited && process.ExitCode == 0 ? ReplacementChange.Success : ReplacementChange.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return ReplacementChange.Cancelled;
        }
        catch
        {
            return ReplacementChange.Failed;
        }
    }

    /// <summary>
    /// Schreibt bzw. entfernt den Eintrag (läuft im erhöhten Hilfsprozess).
    /// Rückgabe: 0 = erledigt, 5 = keine Berechtigung, 1 = anderer Fehler.
    /// </summary>
    public static int Apply(bool enable)
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            if (enable)
            {
                using var key = machine.CreateSubKey(IfeoKeyPath, writable: true);
                key.SetValue(DebuggerValue, $"\"{ExecutablePath}\" {LaunchedArgument}", RegistryValueKind.String);
                return 0;
            }

            bool removeKey;
            using (var key = machine.OpenSubKey(IfeoKeyPath, writable: true))
            {
                if (key == null)
                    return 0;

                // Nur einen SystemWatch-Eintrag entfernen, nie den eines anderen Programms.
                if (key.GetValue(DebuggerValue) is string debugger && IsSystemWatch(ExtractExecutable(debugger)))
                    key.DeleteValue(DebuggerValue, throwOnMissingValue: false);

                removeKey = key.ValueCount == 0 && key.SubKeyCount == 0;
            }

            if (removeKey)
                machine.DeleteSubKey(IfeoKeyPath, throwOnMissingSubKey: false);
            return 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return 5;
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>Holt den Programmpfad aus einer Befehlszeile wie <c>"C:\…\x.exe" --arg</c>.</summary>
    internal static string ExtractExecutable(string commandLine)
    {
        string text = commandLine.Trim();
        if (text.StartsWith("\"", StringComparison.Ordinal))
        {
            int end = text.IndexOf('"', 1);
            return end > 1 ? text.Substring(1, end - 1) : text.Trim('"');
        }

        int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe >= 0)
            return text.Substring(0, exe + 4);

        int space = text.IndexOf(' ');
        return space > 0 ? text.Substring(0, space) : text;
    }

    private static bool IsSystemWatch(string path) =>
        string.Equals(Path.GetFileName(path), Path.GetFileName(ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetFileName(path), "SystemWatch.exe", StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string GetExecutablePath()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            if (process.MainModule?.FileName is { Length: > 0 } file)
                return file;
        }
        catch
        {
            // Fallback unten
        }

        return Assembly.GetEntryAssembly()?.Location ?? "SystemWatch.exe";
    }
}
