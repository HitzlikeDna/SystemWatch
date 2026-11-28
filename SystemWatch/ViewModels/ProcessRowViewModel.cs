using System;
using System.Collections.Generic;
using System.Windows.Media;
using SystemWatch.Infrastructure;
using SystemWatch.Monitoring;

namespace SystemWatch.ViewModels;

/// <summary>
/// Eine Zeile der Prozessliste – entweder ein einzelner Prozess oder alle Prozesse mit demselben
/// Programmnamen zusammengefasst (z. B. „Google Chrome (24)“). Das Objekt bleibt über die
/// Aktualisierungen hinweg erhalten, damit Auswahl und Scrollposition nicht springen.
/// </summary>
public sealed class ProcessRowViewModel(string key) : ObservableObject
{
    private string _name = "";
    private string _imageName = "";
    private string? _imagePath;
    private ImageSource? _icon;
    private ProcessCategory _category;
    private int _count;
    private int _pid;
    private double _cpu = -1;
    private string _cpuText = "";
    private double _cpuHeat;
    private ulong _memoryBytes = ulong.MaxValue;
    private string _memoryText = "";
    private double _memoryHeat;
    private double _gpuSort = -2;
    private string _gpuText = "";
    private double _gpuHeat;

    public string Key { get; } = key;

    public IReadOnlyList<ProcessIdentity> Members { get; private set; } = Array.Empty<ProcessIdentity>();

    public string Name
    {
        get => _name;
        private set => Set(ref _name, value);
    }

    public string ImageName
    {
        get => _imageName;
        private set => Set(ref _imageName, value);
    }

    public string? ImagePath
    {
        get => _imagePath;
        private set => Set(ref _imagePath, value);
    }

    public ImageSource? Icon
    {
        get => _icon;
        private set => Set(ref _icon, value);
    }

    public ProcessCategory Category
    {
        get => _category;
        private set
        {
            if (Set(ref _category, value))
            {
                OnPropertyChanged(nameof(CategoryOrder));
                OnPropertyChanged(nameof(CategoryName));
            }
        }
    }

    public int CategoryOrder => (int)_category;

    public string CategoryName => _category switch
    {
        ProcessCategory.App => "Apps",
        ProcessCategory.Background => "Hintergrundprozesse",
        _ => "Windows-Prozesse",
    };

    public int Count
    {
        get => _count;
        private set
        {
            if (Set(ref _count, value))
                OnPropertyChanged(nameof(CountText));
        }
    }

    public string CountText => _count > 1 ? $"({_count})" : "";

    public int Pid
    {
        get => _pid;
        private set => Set(ref _pid, value);
    }

    public double Cpu
    {
        get => _cpu;
        private set => Set(ref _cpu, value);
    }

    public string CpuText
    {
        get => _cpuText;
        private set => Set(ref _cpuText, value);
    }

    /// <summary>Hinterlegungen neu zeichnen lassen (nach einem Designwechsel).</summary>
    internal void RefreshHeat()
    {
        OnPropertyChanged(nameof(CpuHeat));
        OnPropertyChanged(nameof(MemoryHeat));
        OnPropertyChanged(nameof(GpuHeat));
    }

    public double CpuHeat
    {
        get => _cpuHeat;
        private set => Set(ref _cpuHeat, value);
    }

    public ulong MemoryBytes
    {
        get => _memoryBytes;
        private set => Set(ref _memoryBytes, value);
    }

    public string MemoryText
    {
        get => _memoryText;
        private set => Set(ref _memoryText, value);
    }

    public double MemoryHeat
    {
        get => _memoryHeat;
        private set => Set(ref _memoryHeat, value);
    }

    /// <summary>Sortierwert; -1 wenn keine GPU-Daten verfügbar sind.</summary>
    public double GpuSort
    {
        get => _gpuSort;
        private set => Set(ref _gpuSort, value);
    }

    public string GpuText
    {
        get => _gpuText;
        private set => Set(ref _gpuText, value);
    }

    public double GpuHeat
    {
        get => _gpuHeat;
        private set => Set(ref _gpuHeat, value);
    }

    internal void Update(ProcessGroup group, ulong totalMemory)
    {
        Members = group.Members;
        Name = group.DisplayName;
        ImageName = group.ImageName;
        ImagePath = group.ImagePath;
        Icon = group.Icon;
        Category = group.Category;
        Count = group.Members.Count;
        Pid = group.Members[0].Pid;

        // Auf eine Nachkommastelle runden, damit die Live-Sortierung nicht bei jedem Rauschen umsortiert.
        double cpu = Math.Round(group.Cpu, 1);
        Cpu = cpu;
        CpuText = Format.Percent(cpu);
        CpuHeat = Heat(cpu / 100);

        MemoryBytes = group.PrivateBytes;
        MemoryText = Format.Megabytes(group.PrivateBytes);
        MemoryHeat = totalMemory == 0 ? 0 : Heat((double)group.PrivateBytes / totalMemory);

        if (group.Gpu is { } gpu)
        {
            gpu = Math.Round(Math.Min(100, gpu), 1);
            GpuSort = gpu;
            GpuText = Format.Percent(gpu);
            GpuHeat = Heat(gpu / 100);
        }
        else
        {
            GpuSort = -1;
            GpuText = "–";
            GpuHeat = 0;
        }
    }

    /// <summary>Wurzel-Skala: auch kleine Anteile werden sichtbar, große sättigen nicht sofort.</summary>
    private static double Heat(double fraction) => fraction < 0.002 ? 0 : Math.Round(Math.Sqrt(Math.Min(1, fraction)), 2);
}

/// <summary>Zwischenergebnis: alle Prozesse, die in einer Zeile zusammengefasst werden.</summary>
internal sealed class ProcessGroup
{
    public List<ProcessIdentity> Members { get; } = new(1);
    public string DisplayName { get; private set; } = "";
    public string ImageName { get; private set; } = "";
    public string? ImagePath { get; private set; }
    public ImageSource? Icon { get; private set; }
    public ProcessCategory Category { get; private set; } = ProcessCategory.Windows;
    public double Cpu { get; private set; }
    public ulong PrivateBytes { get; private set; }
    public double? Gpu { get; private set; }

    public void Add(ProcessSample sample)
    {
        if (Members.Count == 0)
        {
            DisplayName = sample.DisplayName;
            ImageName = sample.ImageName;
        }

        Members.Add(sample.Identity);
        ImagePath ??= sample.ImagePath;
        Icon ??= sample.Icon;
        if (sample.Category < Category)
            Category = sample.Category;

        Cpu += sample.CpuPercent;
        PrivateBytes += sample.PrivateBytes;
        if (sample.GpuPercent is { } gpu)
            Gpu = (Gpu ?? 0) + gpu;
    }
}
