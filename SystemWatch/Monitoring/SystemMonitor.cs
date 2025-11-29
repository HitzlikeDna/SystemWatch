using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SystemWatch.Monitoring
{
    public class SystemMonitor : IDisposable
    {
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _ramAvailableCounter;
        private PerformanceCounter _netSentCounter;
        private PerformanceCounter _netReceivedCounter;
        private PerformanceCounter _diskBytesCounter;
        private PerformanceCounter[] _gpuCounters;
        private readonly ulong _totalRamBytes;

        private string _currentDrive = "C:\\";
        private string _currentAdapterInstanceName;

        public SystemMonitor()
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ = _cpuCounter.NextValue();

            _ramAvailableCounter = new PerformanceCounter("Memory", "Available MBytes");

            _totalRamBytes = GetTotalMemoryInBytes();

            InitDiskCounter();
            InitGpuCounters();

            var adapters = GetNetworkAdapters();
            if (adapters.Length > 0)
                SetNetworkAdapter(adapters[0]);

            var drives = GetDrives();
            if (drives.Length > 0)
                SetDrive(drives[0]);
        }

        public string[] GetNetworkAdapters()
        {
            try
            {
                var category = new PerformanceCounterCategory("Network Interface");
                string[] instances = category.GetInstanceNames();

                var filtered = instances
                    .Where(name =>
                        !name.ToLower().Contains("loopback") &&
                        !name.ToLower().Contains("isatap"))
                    .ToArray();

                if (filtered.Length > 0)
                    return filtered;

                return instances;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public void SetNetworkAdapter(string instanceName)
        {
            _currentAdapterInstanceName = instanceName;

            if (string.IsNullOrWhiteSpace(instanceName))
            {
                _netSentCounter = null;
                _netReceivedCounter = null;
                return;
            }

            try
            {
                _netSentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", instanceName);
                _netReceivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", instanceName);
                _ = _netSentCounter.NextValue();
                _ = _netReceivedCounter.NextValue();
            }
            catch
            {
                _netSentCounter = null;
                _netReceivedCounter = null;
            }
        }

        public string[] GetDrives()
        {
            try
            {
                return DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Fixed)
                    .Select(d => d.Name)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public void SetDrive(string driveName)
        {
            if (string.IsNullOrWhiteSpace(driveName))
                return;

            _currentDrive = driveName;
        }

        public SystemStats Read()
        {
            var stats = new SystemStats();

            float cpu = 0;
            try
            {
                cpu = _cpuCounter.NextValue();
            }
            catch { }
            stats.CpuPercent = cpu;

            float availableMB = 0;
            try
            {
                availableMB = _ramAvailableCounter.NextValue();
            }
            catch { }

            double availableBytes = availableMB * 1024 * 1024;
            if (_totalRamBytes > 0)
            {
                stats.RamPercent = (1 - (availableBytes / _totalRamBytes)) * 100.0;
            }

            double gpuUsage = 0;
            if (_gpuCounters != null && _gpuCounters.Length > 0)
            {
                try
                {
                    foreach (var c in _gpuCounters)
                        gpuUsage += c.NextValue();
                    stats.GpuPercent = gpuUsage;
                }
                catch
                {
                    stats.GpuPercent = null;
                }
            }
            else
            {
                stats.GpuPercent = null;
            }

            if (_netSentCounter != null && _netReceivedCounter != null)
            {
                try
                {
                    float sent = _netSentCounter.NextValue();
                    float received = _netReceivedCounter.NextValue();
                    double totalBytesPerSec = sent + received;
                    stats.NetKiloBytesPerSecond = totalBytesPerSec / 1024.0;
                    stats.NetworkAdapterAvailable = true;
                }
                catch
                {
                    stats.NetKiloBytesPerSecond = 0;
                    stats.NetworkAdapterAvailable = false;
                }
            }
            else
            {
                stats.NetKiloBytesPerSecond = 0;
                stats.NetworkAdapterAvailable = false;
            }

            if (_diskBytesCounter != null)
            {
                try
                {
                    double bytes = _diskBytesCounter.NextValue();
                    stats.DiskMegaBytesPerSecond = bytes / (1024.0 * 1024.0);
                }
                catch
                {
                    stats.DiskMegaBytesPerSecond = 0;
                }
            }

            stats.DriveName = _currentDrive;
            try
            {
                var drive = new DriveInfo(_currentDrive);
                if (drive.IsReady)
                {
                    double totalGB = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freeGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double usedGB = totalGB - freeGB;
                    stats.DriveTotalGb = totalGB;
                    stats.DriveFreeGb = freeGB;
                    stats.DriveUsedPercent = totalGB > 0 ? (usedGB / totalGB) * 100.0 : 0;
                    stats.DriveReady = true;
                    stats.DriveError = false;
                }
                else
                {
                    stats.DriveReady = false;
                    stats.DriveError = false;
                }
            }
            catch
            {
                stats.DriveReady = false;
                stats.DriveError = true;
            }

            return stats;
        }

        private void InitDiskCounter()
        {
            try
            {
                var category = new PerformanceCounterCategory("PhysicalDisk");
                string[] instances = category.GetInstanceNames();
                string name = instances.FirstOrDefault(n => n == "_Total") ?? instances.FirstOrDefault() ?? "";
                if (!string.IsNullOrEmpty(name))
                {
                    _diskBytesCounter = new PerformanceCounter("PhysicalDisk", "Disk Bytes/sec", name);
                    _ = _diskBytesCounter.NextValue();
                }
            }
            catch
            {
                _diskBytesCounter = null;
            }
        }

        private void InitGpuCounters()
        {
            try
            {
                var category = new PerformanceCounterCategory("GPU Engine");
                string[] instances = category.GetInstanceNames();
                var list = new List<PerformanceCounter>();
                foreach (var inst in instances)
                {
                    string lower = inst.ToLower();
                    if (lower.Contains("engtype_3d"))
                    {
                        list.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", inst));
                    }
                }
                _gpuCounters = list.ToArray();
                if (_gpuCounters.Length > 0)
                {
                    foreach (var c in _gpuCounters)
                        _ = c.NextValue();
                }
            }
            catch
            {
                _gpuCounters = null;
            }
        }

        private static ulong GetTotalMemoryInBytes()
        {
            MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
                return mem.ullTotalPhys;
            return 0;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public void Dispose()
        {
            try { _cpuCounter?.Dispose(); } catch { }
            try { _ramAvailableCounter?.Dispose(); } catch { }
            try { _netSentCounter?.Dispose(); } catch { }
            try { _netReceivedCounter?.Dispose(); } catch { }
            try { _diskBytesCounter?.Dispose(); } catch { }
            if (_gpuCounters != null)
            {
                foreach (var c in _gpuCounters)
                {
                    try { c.Dispose(); } catch { }
                }
            }
        }
    }
}
