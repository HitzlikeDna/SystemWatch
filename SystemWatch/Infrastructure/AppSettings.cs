using System;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;

namespace SystemWatch.Infrastructure;

/// <summary>Einstellungen unter HKCU\Software\SystemWatch.</summary>
public sealed class AppSettings
{
    private const string KeyPath = @"Software\SystemWatch";

    public static readonly int[] IntervalOptions = [500, 1000, 2000, 5000];
    public static readonly int[] HistoryOptions = [30, 60, 120, 300];

    public int UpdateIntervalMs { get; set; } = 1000;
    public int HistorySeconds { get; set; } = 60;
    public bool SmoothAnimations { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool ShowWindowsProcesses { get; set; }
    public bool GroupProcesses { get; set; } = true;
    public string NetworkAdapterId { get; set; } = "";

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 840;
    public bool WindowMaximized { get; set; }

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key == null)
                return settings;

            settings.UpdateIntervalMs = ReadChoice(key, "UpdateIntervalMs", IntervalOptions, settings.UpdateIntervalMs);
            settings.HistorySeconds = ReadChoice(key, "HistorySeconds", HistoryOptions, settings.HistorySeconds);
            settings.SmoothAnimations = ReadBool(key, "SmoothAnimations", settings.SmoothAnimations);
            settings.AlwaysOnTop = ReadBool(key, "AlwaysOnTop", settings.AlwaysOnTop);
            settings.ShowWindowsProcesses = ReadBool(key, "ShowWindowsProcesses", settings.ShowWindowsProcesses);
            settings.GroupProcesses = ReadBool(key, "GroupProcesses", settings.GroupProcesses);
            settings.NetworkAdapterId = key.GetValue("NetworkAdapterId") as string ?? "";

            settings.WindowLeft = ReadDouble(key, "Left");
            settings.WindowTop = ReadDouble(key, "Top");
            settings.WindowWidth = ReadDouble(key, "Width") ?? settings.WindowWidth;
            settings.WindowHeight = ReadDouble(key, "Height") ?? settings.WindowHeight;
            settings.WindowMaximized = ReadBool(key, "Maximized", false);
        }
        catch
        {
            // Beschädigte Werte → Standardwerte
        }

        return settings;
    }

    public void Save()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue("UpdateIntervalMs", UpdateIntervalMs, RegistryValueKind.DWord);
            key.SetValue("HistorySeconds", HistorySeconds, RegistryValueKind.DWord);
            key.SetValue("SmoothAnimations", SmoothAnimations ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("AlwaysOnTop", AlwaysOnTop ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("ShowWindowsProcesses", ShowWindowsProcesses ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("GroupProcesses", GroupProcesses ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("NetworkAdapterId", NetworkAdapterId ?? "");

            WriteDouble(key, "Left", WindowLeft);
            WriteDouble(key, "Top", WindowTop);
            WriteDouble(key, "Width", WindowWidth);
            WriteDouble(key, "Height", WindowHeight);
            key.SetValue("Maximized", WindowMaximized ? 1 : 0, RegistryValueKind.DWord);
        }
        catch
        {
            // Ohne Schreibrechte einfach nicht speichern.
        }
    }

    private static int ReadChoice(RegistryKey key, string name, int[] allowed, int fallback) =>
        key.GetValue(name) is int value && allowed.Contains(value) ? value : fallback;

    private static bool ReadBool(RegistryKey key, string name, bool fallback) =>
        key.GetValue(name) is int value ? value != 0 : fallback;

    private static double? ReadDouble(RegistryKey key, string name)
    {
        string? text = key.GetValue(name)?.ToString();
        if (string.IsNullOrEmpty(text))
            return null;

        // Ältere Versionen haben mit der aktuellen Kultur gespeichert.
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            return double.IsNaN(value) || double.IsInfinity(value) ? null : value;
        return null;
    }

    private static void WriteDouble(RegistryKey key, string name, double? value)
    {
        if (value is { } v && !double.IsNaN(v) && !double.IsInfinity(v))
            key.SetValue(name, v.ToString("R", CultureInfo.InvariantCulture));
        else
            key.DeleteValue(name, false);
    }
}

/// <summary>Eintrag unter HKCU\…\Run für den Start mit Windows.</summary>
public static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SystemWatch";
    public const string MinimizedArgument = "--minimized";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string { Length: > 0 };
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Gibt <c>false</c> zurück, wenn der Eintrag nicht geschrieben werden konnte.</summary>
    public static bool TrySet(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                string exe = System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";
                key.SetValue(ValueName, $"\"{exe}\" {MinimizedArgument}");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
