using System.Collections.ObjectModel;
using SystemWatch.Charts;
using SystemWatch.Infrastructure;

namespace SystemWatch.ViewModels;

/// <summary>Daten einer Kachel auf der Übersicht (CPU, Arbeitsspeicher, GPU, Netzwerk, Datenträger).</summary>
public sealed class MetricViewModel : ObservableObject
{
    private string _detail = "";
    private string _value = "–";
    private string _secondaryValue = "–";
    private string _caption = "";
    private double _fraction;
    private bool _isAvailable = true;

    public MetricViewModel(string title, int capacity, bool hasSecondarySeries = false)
    {
        Title = title;
        Series = new MetricSeries(capacity);
        SecondarySeries = hasSecondarySeries ? new MetricSeries(capacity) : null;
    }

    public string Title { get; }

    public MetricSeries Series { get; }

    public MetricSeries? SecondarySeries { get; }

    public ObservableCollection<StatItem> Stats { get; } = new();

    /// <summary>Zusatzinfo in der Kopfzeile, z. B. der Prozessorname.</summary>
    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    /// <summary>Großer Hauptwert.</summary>
    public string Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    /// <summary>Zweiter Hauptwert bei Kacheln mit zwei Linien (Upload, Schreiben).</summary>
    public string SecondaryValue
    {
        get => _secondaryValue;
        set => Set(ref _secondaryValue, value);
    }

    /// <summary>Kleiner Text neben dem Hauptwert.</summary>
    public string Caption
    {
        get => _caption;
        set => Set(ref _caption, value);
    }

    /// <summary>Auslastung 0…1 für die Mini-Balken in der Seitenleiste.</summary>
    public double Fraction
    {
        get => _fraction;
        set => Set(ref _fraction, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        set => Set(ref _isAvailable, value);
    }

    public StatItem AddStat(string label)
    {
        var item = new StatItem(label);
        Stats.Add(item);
        return item;
    }

    public void Resize(int capacity)
    {
        Series.Resize(capacity);
        SecondarySeries?.Resize(capacity);
    }
}

public sealed class StatItem(string label) : ObservableObject
{
    private string _value = "–";

    public string Label { get; } = label;

    public string Value
    {
        get => _value;
        set => Set(ref _value, value);
    }
}

public sealed class DriveViewModel(string name) : ObservableObject
{
    private string _label = "";
    private string _usage = "";
    private string _free = "";
    private double _fraction;

    public string Name { get; } = name;

    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    public string Usage
    {
        get => _usage;
        set => Set(ref _usage, value);
    }

    public string Free
    {
        get => _free;
        set => Set(ref _free, value);
    }

    public double Fraction
    {
        get => _fraction;
        set => Set(ref _fraction, value);
    }
}

public sealed class TopProcessViewModel : ObservableObject
{
    private string _name = "";
    private System.Windows.Media.ImageSource? _icon;
    private string _cpu = "";
    private string _memory = "";
    private double _fraction;
    private bool _isVisible;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public System.Windows.Media.ImageSource? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    public string Cpu
    {
        get => _cpu;
        set => Set(ref _cpu, value);
    }

    public string Memory
    {
        get => _memory;
        set => Set(ref _memory, value);
    }

    public double Fraction
    {
        get => _fraction;
        set => Set(ref _fraction, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }
}
