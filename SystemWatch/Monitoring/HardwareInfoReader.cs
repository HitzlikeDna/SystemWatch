using System;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SystemWatch.Monitoring;

/// <summary>Liest Prozessor- und Grafikkartennamen aus der Registry (schneller als WMI).</summary>
internal static class HardwareInfoReader
{
    private const string CpuKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static HardwareInfo Read()
    {
        var info = new HardwareInfo { CpuName = ReadCpuName() };
        ReadGpu(info);
        return info;
    }

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(CpuKey);
            if (key?.GetValue("ProcessorNameString") is string name && name.Trim().Length > 0)
                return Tidy(name);
        }
        catch
        {
            // Registry nicht lesbar – Standardname verwenden.
        }

        return "Prozessor";
    }

    private static void ReadGpu(HardwareInfo info)
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (classKey == null)
                return;

            ulong bestMemory = 0;
            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                if (subKeyName.Length != 4 || !int.TryParse(subKeyName, out _))
                    continue;

                using var adapter = classKey.OpenSubKey(subKeyName);
                if (adapter?.GetValue("DriverDesc") is not string name)
                    continue;
                if (name.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                ulong memory = ReadAdapterMemory(adapter);
                if (info.GpuName == null || memory > bestMemory)
                {
                    info.GpuName = Tidy(name);
                    info.GpuDedicatedTotalBytes = memory > 0 ? memory : null;
                    bestMemory = memory;
                }
            }
        }
        catch
        {
            // Kein Zugriff – GPU-Name bleibt leer.
        }
    }

    private static ulong ReadAdapterMemory(RegistryKey adapter)
    {
        switch (adapter.GetValue("HardwareInformation.qwMemorySize"))
        {
            case long qw: return (ulong)qw;
            case byte[] bytes when bytes.Length >= 8: return BitConverter.ToUInt64(bytes, 0);
        }

        switch (adapter.GetValue("HardwareInformation.MemorySize"))
        {
            case int dw: return (uint)dw;
            case byte[] bytes when bytes.Length >= 4: return BitConverter.ToUInt32(bytes, 0);
        }

        return 0;
    }

    /// <summary>Entfernt Marken-Rauschen wie "(R)", "(TM)" und doppelte Leerzeichen.</summary>
    private static string Tidy(string name)
    {
        name = Regex.Replace(name, @"\((R|TM|C)\)", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s+CPU\s+@.*$", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s{2,}", " ");
        return name.Trim();
    }
}
