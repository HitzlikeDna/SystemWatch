using System.Collections.ObjectModel;
using System.Reflection;
using SystemWatch.Charts;
using SystemWatch.Infrastructure;
using SystemWatch.Monitoring;

namespace SystemWatch.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IUserInteraction _ui;
    private bool _autostart;

    public SettingsViewModel(AppSettings settings, IUserInteraction ui)
    {
        _settings = settings;
        _ui = ui;
        _autostart = Infrastructure.Autostart.IsEnabled;
        FrameClock.Enabled = settings.SmoothAnimations;
        RefreshAdapters();

        // Jede Änderung sofort sichern, nicht erst beim Schließen.
        PropertyChanged += (_, _) => _settings.Save();
    }

    public ObservableCollection<AdapterOptionViewModel> Adapters { get; } = new();

    public string Version { get; } =
        "Version " + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "2.0.0");

    public int UpdateIntervalMs
    {
        get => _settings.UpdateIntervalMs;
        set
        {
            if (_settings.UpdateIntervalMs == value)
                return;
            _settings.UpdateIntervalMs = value;
            OnPropertyChanged();
        }
    }

    public int HistorySeconds
    {
        get => _settings.HistorySeconds;
        set
        {
            if (_settings.HistorySeconds == value)
                return;
            _settings.HistorySeconds = value;
            OnPropertyChanged();
        }
    }

    public bool SmoothAnimations
    {
        get => _settings.SmoothAnimations;
        set
        {
            if (_settings.SmoothAnimations == value)
                return;
            _settings.SmoothAnimations = value;
            FrameClock.Enabled = value;
            OnPropertyChanged();
        }
    }

    public bool AlwaysOnTop
    {
        get => _settings.AlwaysOnTop;
        set
        {
            if (_settings.AlwaysOnTop == value)
                return;
            _settings.AlwaysOnTop = value;
            OnPropertyChanged();
        }
    }

    public bool Autostart
    {
        get => _autostart;
        set
        {
            if (_autostart == value)
                return;

            if (!Infrastructure.Autostart.TrySet(value))
            {
                _ui.ShowToast("Der Autostart-Eintrag konnte nicht geändert werden.");
                OnPropertyChanged();
                return;
            }

            _autostart = value;
            OnPropertyChanged();
        }
    }

    public string NetworkAdapterId
    {
        get => _settings.NetworkAdapterId;
        private set
        {
            if (_settings.NetworkAdapterId == value)
                return;
            _settings.NetworkAdapterId = value;
            OnPropertyChanged();
        }
    }

    public void RefreshAdapters()
    {
        Adapters.Clear();
        Adapters.Add(new AdapterOptionViewModel(this, "", "Automatisch", "Aktive Internetverbindung verwenden"));
        foreach (var adapter in NetworkSampler.GetAdapters())
            Adapters.Add(new AdapterOptionViewModel(this, adapter.Id, adapter.Name, adapter.Description));

        // Gespeicherter Adapter nicht mehr vorhanden → automatisch.
        bool known = false;
        foreach (var option in Adapters)
            known |= option.Id == NetworkAdapterId;
        if (!known)
            NetworkAdapterId = "";

        foreach (var option in Adapters)
            option.Refresh();
    }

    internal void SelectAdapter(string id)
    {
        NetworkAdapterId = id;
        foreach (var option in Adapters)
            option.Refresh();
    }
}

public sealed class AdapterOptionViewModel(SettingsViewModel owner, string id, string name, string description) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; } = description;

    public bool IsSelected
    {
        get => owner.NetworkAdapterId == Id;
        set
        {
            if (value)
                owner.SelectAdapter(Id);
        }
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsSelected));
}
