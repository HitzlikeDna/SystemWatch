using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace SystemWatch.Monitoring;

/// <summary>Eine vollständige Messung aller Werte zu einem Zeitpunkt.</summary>
public sealed class SystemSnapshot
{
    /// <summary>Zeitpunkt der Messung (Stopwatch-Ticks).</summary>
    public long Timestamp { get; set; }

    /// <summary>Geplanter Abstand zur nächsten Messung in Sekunden.</summary>
    public double IntervalSeconds { get; set; }

    public HardwareInfo Hardware { get; set; } = HardwareInfo.Empty;

    // CPU
    public double CpuPercent { get; set; }
    public double? CpuFrequencyMhz { get; set; }
    public TimeSpan Uptime { get; set; }

    // Arbeitsspeicher
    public ulong MemoryTotalBytes { get; set; }
    public ulong MemoryAvailableBytes { get; set; }
    public ulong CommitUsedBytes { get; set; }
    public ulong CommitLimitBytes { get; set; }
    public ulong MemoryUsedBytes => MemoryTotalBytes > MemoryAvailableBytes ? MemoryTotalBytes - MemoryAvailableBytes : 0;
    public double MemoryPercent => MemoryTotalBytes == 0 ? 0 : MemoryUsedBytes * 100.0 / MemoryTotalBytes;

    // GPU
    public double? GpuPercent { get; set; }
    public ulong? GpuDedicatedUsedBytes { get; set; }

    // Netzwerk
    public NetworkAdapterInfo? NetworkAdapter { get; set; }
    public double NetworkReceiveBytesPerSecond { get; set; }
    public double NetworkSendBytesPerSecond { get; set; }

    // Datenträger
    public double DiskReadBytesPerSecond { get; set; }
    public double DiskWriteBytesPerSecond { get; set; }
    public double? DiskActivePercent { get; set; }
    public IReadOnlyList<DriveSnapshot> Drives { get; set; } = Array.Empty<DriveSnapshot>();

    // Prozesse
    public IReadOnlyList<ProcessSample> Processes { get; set; } = Array.Empty<ProcessSample>();
    public int ProcessCount { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
}

/// <summary>Werte, die sich während der Laufzeit nicht ändern.</summary>
public sealed class HardwareInfo
{
    public static readonly HardwareInfo Empty = new();

    public string CpuName { get; set; } = "Prozessor";
    public int LogicalProcessors { get; set; } = Environment.ProcessorCount;
    public string? GpuName { get; set; }
    public ulong? GpuDedicatedTotalBytes { get; set; }
}

public sealed class NetworkAdapterInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "";
    public long LinkSpeedBitsPerSecond { get; set; }
}

public sealed class DriveSnapshot
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public ulong TotalBytes { get; set; }
    public ulong FreeBytes { get; set; }
    public double UsedPercent => TotalBytes == 0 ? 0 : (TotalBytes - FreeBytes) * 100.0 / TotalBytes;
}

public enum ProcessCategory
{
    App = 0,
    Background = 1,
    Windows = 2,
}

/// <summary>Eindeutige Identität eines Prozesses (PIDs werden von Windows wiederverwendet).</summary>
public readonly struct ProcessIdentity : IEquatable<ProcessIdentity>
{
    public ProcessIdentity(int pid, long createTime)
    {
        Pid = pid;
        CreateTime = createTime;
    }

    public int Pid { get; }
    public long CreateTime { get; }

    public bool Equals(ProcessIdentity other) => Pid == other.Pid && CreateTime == other.CreateTime;
    public override bool Equals(object? obj) => obj is ProcessIdentity other && Equals(other);
    public override int GetHashCode() => unchecked((Pid * 397) ^ CreateTime.GetHashCode());
}

public sealed class ProcessSample
{
    public ProcessIdentity Identity { get; set; }
    public int Pid => Identity.Pid;
    public string ImageName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? ImagePath { get; set; }
    public ImageSource? Icon { get; set; }
    public ProcessCategory Category { get; set; }
    public double CpuPercent { get; set; }
    public ulong PrivateBytes { get; set; }
    public double? GpuPercent { get; set; }
    public int ThreadCount { get; set; }
}
