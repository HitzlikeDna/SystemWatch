using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SystemWatch.Native;

namespace SystemWatch.Monitoring;

/// <summary>Systemweite Leistungsindikatoren: CPU, Arbeitsspeicher, Datenträger, Laufwerke.</summary>
internal sealed class SystemSampler : IDisposable
{
    private static readonly TimeSpan DriveRefreshInterval = TimeSpan.FromSeconds(5);

    private readonly PerformanceCounter? _cpu;
    private readonly PerformanceCounter? _cpuPerformance;
    private readonly PerformanceCounter? _cpuBaseFrequency;
    private readonly CounterCategoryReader _disks =
        new("PhysicalDisk", "disk read bytes/sec", "disk write bytes/sec", "% idle time");

    private IReadOnlyList<DriveSnapshot> _drives = Array.Empty<DriveSnapshot>();
    private DateTime _lastDriveRefresh = DateTime.MinValue;

    public SystemSampler()
    {
        // "% Processor Utility" entspricht dem Wert im Windows-Task-Manager,
        // "% Processor Time" ist der Fallback für ältere Systeme.
        _cpu = TryCreate("Processor Information", "% Processor Utility", "_Total")
               ?? TryCreate("Processor", "% Processor Time", "_Total");
        _cpuPerformance = TryCreate("Processor Information", "% Processor Performance", "_Total");
        _cpuBaseFrequency = TryCreate("Processor Information", "Processor Frequency", "_Total");
        _disks.Read((_, _, _) => { }); // erste Messung als Basis
    }

    public void Sample(SystemSnapshot snapshot)
    {
        snapshot.CpuPercent = Clamp(Next(_cpu), 0, 100);

        double performance = Next(_cpuPerformance);
        double baseMhz = Next(_cpuBaseFrequency);
        snapshot.CpuFrequencyMhz = performance > 0 && baseMhz > 0 ? baseMhz * performance / 100.0 : null;

        snapshot.Uptime = TimeSpan.FromMilliseconds(NativeMethods.GetTickCount64());

        var memory = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref memory))
        {
            snapshot.MemoryTotalBytes = memory.ullTotalPhys;
            snapshot.MemoryAvailableBytes = memory.ullAvailPhys;
            snapshot.CommitLimitBytes = memory.ullTotalPageFile;
            snapshot.CommitUsedBytes = memory.ullTotalPageFile - memory.ullAvailPageFile;
        }

        // Datenrate über alle Laufwerke; „Aktive Zeit“ des am stärksten ausgelasteten Laufwerks
        // (der Mittelwert über alle Laufwerke wäre fast immer nahe 0 %).
        double read = 0, write = 0, busiest = -1;
        _disks.Read((counter, instance, value) =>
        {
            bool total = instance == "_Total";
            switch (counter)
            {
                case 0 when total: read = value; break;
                case 1 when total: write = value; break;
                case 2 when !total: busiest = Math.Max(busiest, Clamp(100 - value, 0, 100)); break;
            }
        });

        snapshot.DiskReadBytesPerSecond = Math.Max(0, read);
        snapshot.DiskWriteBytesPerSecond = Math.Max(0, write);
        snapshot.DiskActivePercent = busiest >= 0 ? busiest : null;

        if (DateTime.UtcNow - _lastDriveRefresh > DriveRefreshInterval)
        {
            _drives = ReadDrives();
            _lastDriveRefresh = DateTime.UtcNow;
        }

        snapshot.Drives = _drives;
    }

    private static IReadOnlyList<DriveSnapshot> ReadDrives()
    {
        var result = new List<DriveSnapshot>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed)
                    continue;

                try
                {
                    if (!drive.IsReady)
                        continue;

                    result.Add(new DriveSnapshot
                    {
                        Name = drive.Name.TrimEnd('\\'),
                        Label = drive.VolumeLabel,
                        TotalBytes = (ulong)drive.TotalSize,
                        FreeBytes = (ulong)drive.AvailableFreeSpace,
                    });
                }
                catch (IOException)
                {
                    // Laufwerk wurde zwischenzeitlich entfernt.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }

        return result;
    }

    private static PerformanceCounter? TryCreate(string category, string counter, string instance)
    {
        try
        {
            var pc = new PerformanceCounter(category, counter, instance, readOnly: true);
            _ = pc.NextValue(); // erster Wert ist immer 0
            return pc;
        }
        catch
        {
            return null;
        }
    }

    private static double Next(PerformanceCounter? counter)
    {
        if (counter == null)
            return 0;

        try
        {
            return counter.NextValue();
        }
        catch
        {
            return 0;
        }
    }

    private static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

    public void Dispose()
    {
        _cpu?.Dispose();
        _cpuPerformance?.Dispose();
        _cpuBaseFrequency?.Dispose();
    }
}
