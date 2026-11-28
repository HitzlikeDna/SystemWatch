using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading.Tasks;
using SystemWatch.Charts;
using SystemWatch.Infrastructure;
using SystemWatch.Monitoring;

namespace SystemWatch.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IUserInteraction _ui;
    private bool _autostart;
    private ReplacementStatus _replacement;
    private bool _replacementBusy;

    public SettingsViewModel(AppSettings settings, IUserInteraction ui)
    {
        _settings = settings;
        _ui = ui;
        _autostart = Infrastructure.Autostart.IsEnabled;
        _replacement = TaskManagerReplacement.Query();
        FrameClock.Enabled = settings.SmoothAnimations;
        RefreshAdapters();
        RemoveStaleReplacementCommand = new RelayCommand(() => _ = ChangeReplacementAsync(false));

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

    public AppTheme Theme
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme == value)
                return;
            _settings.Theme = value;
            ThemeManager.Mode = value;
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

    // ───────────── Task-Manager ersetzen ─────────────

    /// <summary>Schalter: Windows startet SystemWatch anstelle des Task-Managers.</summary>
    public bool ReplaceTaskManager
    {
        get => _replacement.State == ReplacementState.Active;
        set
        {
            if (value != ReplaceTaskManager)
                _ = ChangeReplacementAsync(value);
        }
    }

    /// <summary>Während Windows nach Adminrechten fragt, ist der Schalter gesperrt.</summary>
    public bool CanChangeReplacement => !_replacementBusy;

    /// <summary>Wo Windows SystemWatch startet – Hinweis, die Datei nicht zu verschieben.</summary>
    public string ReplacementPath => TaskManagerReplacement.ExecutablePath;

    public string? ReplacementWarning => _replacement.State switch
    {
        ReplacementState.OtherLocation when _replacement.IsBroken =>
            $"Achtung: Windows versucht, SystemWatch von einem alten Ort zu starten ({_replacement.Path}). " +
            "Dort liegt keine Datei mehr, deshalb öffnet sich der Task-Manager zurzeit gar nicht. " +
            "Schalter einschalten, um diesen Ort zu verwenden, oder den Eintrag entfernen.",
        ReplacementState.OtherLocation =>
            $"Eine andere SystemWatch-Kopie ersetzt den Task-Manager ({_replacement.Path}). " +
            "Einschalten verwendet stattdessen diese Kopie.",
        ReplacementState.OtherProgram =>
            $"Ein anderes Programm ersetzt den Task-Manager bereits ({_replacement.Path}). Einschalten ersetzt diesen Eintrag.",
        _ => null,
    };

    /// <summary>Verwaister Eintrag einer verschobenen SystemWatch-Kopie lässt sich entfernen.</summary>
    public bool CanRemoveStaleReplacement => _replacement.State == ReplacementState.OtherLocation && !_replacementBusy;

    public RelayCommand RemoveStaleReplacementCommand { get; }

    /// <summary>Liest den Zustand neu (er kann sich auch außerhalb von SystemWatch ändern).</summary>
    public void RefreshReplacement()
    {
        if (_replacementBusy)
            return;
        _replacement = TaskManagerReplacement.Query();
        RaiseReplacementChanged();
    }

    private async Task ChangeReplacementAsync(bool enable)
    {
        if (_replacementBusy)
            return;

        _replacementBusy = true;
        RaiseReplacementChanged();

        var result = await TaskManagerReplacement.SetAsync(enable);

        _replacementBusy = false;
        _replacement = TaskManagerReplacement.Query();
        RaiseReplacementChanged();

        bool active = _replacement.State == ReplacementState.Active;
        switch (result)
        {
            case ReplacementChange.Cancelled:
                _ui.ShowToast("Abgebrochen – am Task-Manager wurde nichts geändert.");
                break;
            case ReplacementChange.Failed:
                _ui.ShowToast(enable
                    ? "Der Task-Manager konnte nicht ersetzt werden. Dafür sind Administratorrechte nötig."
                    : "Der Eintrag konnte nicht entfernt werden. Dafür sind Administratorrechte nötig.");
                break;
            default:
                _ui.ShowToast(active
                    ? "Strg+Umschalt+Esc und die Taskleiste öffnen jetzt SystemWatch."
                    : "Der Windows-Task-Manager ist wieder aktiv.");
                break;
        }
    }

    private void RaiseReplacementChanged()
    {
        OnPropertyChanged(nameof(ReplaceTaskManager));
        OnPropertyChanged(nameof(CanChangeReplacement));
        OnPropertyChanged(nameof(ReplacementWarning));
        OnPropertyChanged(nameof(CanRemoveStaleReplacement));
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
