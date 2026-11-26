using System;
using System.Globalization;

namespace SystemWatch.Infrastructure;

/// <summary>Einheitliche, deutschsprachige Formatierung aller angezeigten Werte.</summary>
public static class Format
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB"];

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>"23 %" – für große Anzeigen.</summary>
    public static string PercentWhole(double value) => Math.Round(value).ToString("0", Culture) + " %";

    /// <summary>"0,4 %" unter 10, sonst "23 %" – für Tabellen und Tooltips.</summary>
    public static string Percent(double value) =>
        (value > 0 && value < 9.95 ? value.ToString("0.0", Culture) : value.ToString("0", Culture)) + " %";

    /// <summary>"512 KB", "1,2 MB", "31,9 GB" (Basis 1024).</summary>
    public static string Bytes(double bytes)
    {
        if (bytes < 0 || double.IsNaN(bytes))
            bytes = 0;

        int unit = 0;
        while (bytes >= 1000 && unit < ByteUnits.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        string number = unit == 0 || bytes >= 100
            ? bytes.ToString("0", Culture)
            : bytes.ToString("0.0", Culture);
        return number + " " + ByteUnits[unit];
    }

    public static string Speed(double bytesPerSecond) => Bytes(bytesPerSecond) + "/s";

    /// <summary>"1.234,5 MB" – wie die Speicherspalte im Task-Manager.</summary>
    public static string Megabytes(double bytes) => (bytes / (1024.0 * 1024.0)).ToString("N1", Culture) + " MB";

    public static string Gigabytes(double bytes) => (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.0", Culture) + " GB";

    /// <summary>"20,4 / 31,9 GB" – kompakt für „belegt von gesamt“.</summary>
    public static string GigabytesOf(double used, double total) =>
        (used / (1024.0 * 1024.0 * 1024.0)).ToString("0.0", Culture) + " / " + Gigabytes(total);

    public static string Frequency(double megahertz) => (megahertz / 1000).ToString("0.00", Culture) + " GHz";

    public static string LinkSpeed(long bitsPerSecond) => bitsPerSecond switch
    {
        <= 0 => "–",
        >= 1_000_000_000 => (bitsPerSecond / 1e9).ToString("0.#", Culture) + " Gbit/s",
        _ => (bitsPerSecond / 1e6).ToString("0", Culture) + " Mbit/s",
    };

    /// <summary>"3:04:12:33" (Tage:Stunden:Minuten:Sekunden) wie im Task-Manager.</summary>
    public static string Uptime(TimeSpan time) =>
        string.Format(Culture, "{0}:{1:00}:{2:00}:{3:00}", (int)time.TotalDays, time.Hours, time.Minutes, time.Seconds);

    public static string Integer(long value) => value.ToString("N0", Culture);

    /// <summary>"jetzt", "vor 12 s", "vor 2:30 min".</summary>
    public static string Ago(double seconds)
    {
        if (seconds < 0.5)
            return "jetzt";
        if (seconds < 60)
            return "vor " + Math.Round(seconds).ToString("0", Culture) + " s";

        var span = TimeSpan.FromSeconds(Math.Round(seconds));
        return string.Format(Culture, "vor {0}:{1:00} min", (int)span.TotalMinutes, span.Seconds);
    }

    public static string Duration(int seconds) => seconds < 60
        ? seconds.ToString(Culture) + " s"
        : (seconds / 60).ToString(Culture) + " min";
}
