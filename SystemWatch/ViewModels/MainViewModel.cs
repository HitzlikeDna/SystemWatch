using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;
using SystemWatch.Infrastructure;
using SystemWatch.Monitoring;

namespace SystemWatch.ViewModels;

public enum Page
{
    Overview,
    Processes,
    Settings,
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int TopProcessCount = 6;

    private readonly AppSettings _settings;
    private readonly MonitorService _monitor;
    private readonly Dispatcher _dispatcher;

    private readonly StatItem _cpuProcesses, _cpuThreads, _cpuHandles, _cpuUptime;
    private readonly StatItem _memoryUsed, _memoryAvailable, _memoryCommitted;
    private readonly StatItem _gpuMemory;
    private readonly StatItem _networkKind, _networkSpeed;
    private readonly StatItem _diskActive;

    private Page _page = Page.Overview;
    private string _systemSummary = "Messung wird gestartet …";
    private bool _isWaitingForData = true;
    private int _pendingSnapshots;

    public MainViewModel(AppSettings settings, IUserInteraction ui, Dispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;

        int capacity = HistoryCapacity;
        Cpu = new MetricViewModel("CPU", capacity);
        Memory = new MetricViewModel("Arbeitsspeicher", capacity);
        Gpu = new MetricViewModel("GPU", capacity);
        Network = new MetricViewModel("Netzwerk", capacity, hasSecondarySeries: true);
        Disk = new MetricViewModel("Datenträger", capacity, hasSecondarySeries: true);

        _cpuProcesses = Cpu.AddStat("Prozesse");
        _cpuThreads = Cpu.AddStat("Threads");
        _cpuHandles = Cpu.AddStat("Handles");
        _cpuUptime = Cpu.AddStat("Betriebszeit");

        _memoryUsed = Memory.AddStat("In Verwendung");
        _memoryAvailable = Memory.AddStat("Verfügbar");
        _memoryCommitted = Memory.AddStat("Zugesichert");

        _gpuMemory = Gpu.AddStat("Dedizierter GPU-Speicher");

        _networkKind = Network.AddStat("Verbindung");
        _networkSpeed = Network.AddStat("Geschwindigkeit");

        _diskActive = Disk.AddStat("Aktive Zeit (höchste)");
        Disk.Detail = "Alle Datenträger";

        for (int i = 0; i < TopProcessCount; i++)
            TopProcesses.Add(new TopProcessViewModel());

        Processes = new ProcessListViewModel(settings, ui);
        Settings = new SettingsViewModel(settings, ui);
        Settings.PropertyChanged += OnSettingsChanged;

        NavigateCommand = new RelayCommand(p =>
        {
            if (p is Page page || Enum.TryParse(p?.ToString(), out page))
                CurrentPage = page;
        });

        _monitor = new MonitorService(settings.UpdateIntervalMs, settings.NetworkAdapterId);
        _monitor.SnapshotReady += OnSnapshotReady;
        _monitor.Start();
    }

    public MetricViewModel Cpu { get; }
    public MetricViewModel Memory { get; }
    public MetricViewModel Gpu { get; }
    public MetricViewModel Network { get; }
    public MetricViewModel Disk { get; }

    public ObservableCollection<DriveViewModel> Drives { get; } = new();
    public ObservableCollection<TopProcessViewModel> TopProcesses { get; } = new();

    public ProcessListViewModel Processes { get; }
    public SettingsViewModel Settings { get; }

    public RelayCommand NavigateCommand { get; }

    public Page CurrentPage
    {
        get => _page;
        set => Set(ref _page, value);
    }

    /// <summary>z. B. „AMD Ryzen 7 5800X · 31,9 GB RAM · NVIDIA GeForce RTX 3070“.</summary>
    public string SystemSummary
    {
        get => _systemSummary;
        private set => Set(ref _systemSummary, value);
    }

    public bool IsWaitingForData
    {
        get => _isWaitingForData;
        private set => Set(ref _isWaitingForData, value);
    }

    public string HistoryText => "Verlauf: " + Format.Duration(_settings.HistorySeconds);

    private int HistoryCapacity => Math.Max(2, _settings.HistorySeconds * 1000 / _settings.UpdateIntervalMs + 1);

    // ───────────── Messwerte übernehmen ─────────────

    private void OnSnapshotReady(SystemSnapshot snapshot)
    {
        // Falls die Oberfläche hinterherhinkt, keine Messungen aufstauen.
        if (System.Threading.Interlocked.Increment(ref _pendingSnapshots) > 2)
        {
            System.Threading.Interlocked.Decrement(ref _pendingSnapshots);
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            System.Threading.Interlocked.Decrement(ref _pendingSnapshots);
            Apply(snapshot);
        }));
    }

    private void Apply(SystemSnapshot s)
    {
        // Raten (Netzwerk, GPU) brauchen zwei Messungen – die erste ist immer 0 und
        // würde als falscher Einbruch im Graphen stehen.
        bool hasRates = !IsWaitingForData;
        IsWaitingForData = false;
        long t = s.Timestamp;
        double interval = s.IntervalSeconds;
        var hw = s.Hardware;

        SystemSummary = string.Join(" · ", new[]
        {
            hw.CpuName,
            Format.Gigabytes(s.MemoryTotalBytes) + " RAM",
            hw.GpuName,
        }.Where(part => !string.IsNullOrEmpty(part)));

        // CPU
        Cpu.Series.Push(s.CpuPercent, t, interval);
        Cpu.Value = Format.PercentWhole(s.CpuPercent);
        Cpu.Fraction = s.CpuPercent / 100;
        Cpu.Detail = hw.CpuName;
        Cpu.Caption = s.CpuFrequencyMhz is { } mhz ? Format.Frequency(mhz) : $"{hw.LogicalProcessors} logische Prozessoren";
        _cpuProcesses.Value = Format.Integer(s.ProcessCount);
        _cpuThreads.Value = Format.Integer(s.ThreadCount);
        _cpuHandles.Value = Format.Integer(s.HandleCount);
        _cpuUptime.Value = Format.Uptime(s.Uptime);

        // Arbeitsspeicher
        Memory.Series.Push(s.MemoryPercent, t, interval);
        Memory.Value = Format.PercentWhole(s.MemoryPercent);
        Memory.Fraction = s.MemoryPercent / 100;
        Memory.Detail = Format.Gigabytes(s.MemoryTotalBytes) + " gesamt";
        Memory.Caption = Format.GigabytesOf(s.MemoryUsedBytes, s.MemoryTotalBytes);
        _memoryUsed.Value = Format.Gigabytes(s.MemoryUsedBytes);
        _memoryAvailable.Value = Format.Gigabytes(s.MemoryAvailableBytes);
        _memoryCommitted.Value = Format.GigabytesOf(s.CommitUsedBytes, s.CommitLimitBytes);

        // GPU
        Gpu.IsAvailable = s.GpuPercent.HasValue;
        if (hasRates)
            Gpu.Series.Push(s.GpuPercent ?? 0, t, interval);
        Gpu.Value = s.GpuPercent is { } gpu ? Format.PercentWhole(gpu) : "–";
        Gpu.Fraction = (s.GpuPercent ?? 0) / 100;
        Gpu.Detail = hw.GpuName ?? "";
        Gpu.Caption = s.GpuPercent.HasValue ? "Am stärksten ausgelastete Engine" : "Keine GPU-Daten verfügbar";
        _gpuMemory.Value = s.GpuDedicatedUsedBytes is { } vram
            ? hw.GpuDedicatedTotalBytes is { } total
                ? Format.GigabytesOf(vram, total)
                : Format.Gigabytes(vram)
            : "–";

        // Netzwerk
        if (hasRates)
        {
            Network.Series.Push(s.NetworkReceiveBytesPerSecond, t, interval);
            Network.SecondarySeries!.Push(s.NetworkSendBytesPerSecond, t, interval);
        }

        Network.Value = Format.Speed(s.NetworkReceiveBytesPerSecond);
        Network.SecondaryValue = Format.Speed(s.NetworkSendBytesPerSecond);
        Network.IsAvailable = s.NetworkAdapter != null;
        Network.Detail = s.NetworkAdapter?.Name ?? "Keine Verbindung";
        _networkKind.Value = s.NetworkAdapter?.Kind ?? "–";
        _networkSpeed.Value = s.NetworkAdapter is { } adapter ? Format.LinkSpeed(adapter.LinkSpeedBitsPerSecond) : "–";

        // Datenträger
        Disk.Series.Push(s.DiskReadBytesPerSecond, t, interval);
        Disk.SecondarySeries!.Push(s.DiskWriteBytesPerSecond, t, interval);
        Disk.Value = Format.Speed(s.DiskReadBytesPerSecond);
        Disk.SecondaryValue = Format.Speed(s.DiskWriteBytesPerSecond);
        _diskActive.Value = s.DiskActivePercent is { } active ? Format.PercentWhole(active) : "–";
        UpdateDrives(s.Drives);

        Processes.Update(s);
        UpdateTopProcesses(s);
    }

    private void UpdateDrives(IReadOnlyList<DriveSnapshot> drives)
    {
        for (int i = 0; i < drives.Count; i++)
        {
            var drive = drives[i];
            if (i >= Drives.Count || Drives[i].Name != drive.Name)
            {
                Drives.Clear();
                foreach (var d in drives)
                    Drives.Add(new DriveViewModel(d.Name));
            }

            var vm = Drives[i];
            vm.Label = string.IsNullOrWhiteSpace(drive.Label) ? "Lokaler Datenträger" : drive.Label;
            vm.Usage = Format.PercentWhole(drive.UsedPercent);
            vm.Free = $"{Format.Bytes(drive.FreeBytes)} frei von {Format.Bytes(drive.TotalBytes)}";
            vm.Fraction = drive.UsedPercent / 100;
        }

        while (Drives.Count > drives.Count)
            Drives.RemoveAt(Drives.Count - 1);
    }

    private void UpdateTopProcesses(SystemSnapshot s)
    {
        var top = s.Processes
            .GroupBy(p => p.ImageName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                First = g.First(),
                Cpu = g.Sum(p => p.CpuPercent),
                Memory = g.Aggregate(0UL, (sum, p) => sum + p.PrivateBytes),
                Count = g.Count(),
            })
            .OrderByDescending(g => Math.Round(g.Cpu, 1))
            .ThenByDescending(g => g.Memory)
            .Take(TopProcessCount)
            .ToList();

        double scale = Math.Max(5, top.Count > 0 ? top[0].Cpu : 0);
        for (int i = 0; i < TopProcesses.Count; i++)
        {
            var item = TopProcesses[i];
            if (i >= top.Count)
            {
                item.IsVisible = false;
                continue;
            }

            var entry = top[i];
            item.IsVisible = true;
            item.Name = entry.Count > 1 ? $"{entry.First.DisplayName} ({entry.Count})" : entry.First.DisplayName;
            item.Icon = s.Processes.FirstOrDefault(p => p.Icon != null &&
                string.Equals(p.ImageName, entry.First.ImageName, StringComparison.OrdinalIgnoreCase))?.Icon;
            item.Cpu = Format.Percent(Math.Round(entry.Cpu, 1));
            item.Memory = Format.Megabytes(entry.Memory);
            item.Fraction = entry.Cpu / scale;
        }
    }

    // ───────────── Einstellungen ─────────────

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsViewModel.UpdateIntervalMs):
                _monitor.IntervalMs = _settings.UpdateIntervalMs;
                ResizeHistory();
                break;
            case nameof(SettingsViewModel.HistorySeconds):
                ResizeHistory();
                break;
            case nameof(SettingsViewModel.NetworkAdapterId):
                _monitor.NetworkAdapterId = _settings.NetworkAdapterId;
                break;
        }
    }

    private void ResizeHistory()
    {
        int capacity = HistoryCapacity;
        foreach (var metric in new[] { Cpu, Memory, Gpu, Network, Disk })
            metric.Resize(capacity);
        OnPropertyChanged(nameof(HistoryText));
    }

    public void Dispose()
    {
        _monitor.SnapshotReady -= OnSnapshotReady;
        _monitor.Dispose();
    }
}
