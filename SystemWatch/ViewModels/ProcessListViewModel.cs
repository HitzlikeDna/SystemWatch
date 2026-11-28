using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using SystemWatch.Infrastructure;
using SystemWatch.Monitoring;

namespace SystemWatch.ViewModels;

/// <summary>Was die Oberfläche für Rückfragen und Hinweise anbietet.</summary>
public interface IUserInteraction
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    void ShowToast(string message);
}

public sealed class ProcessListViewModel : ObservableObject
{
    private static readonly string[] LiveProperties =
    [
        nameof(ProcessRowViewModel.CategoryOrder),
        nameof(ProcessRowViewModel.Name),
        nameof(ProcessRowViewModel.Pid),
        nameof(ProcessRowViewModel.Cpu),
        nameof(ProcessRowViewModel.MemoryBytes),
        nameof(ProcessRowViewModel.GpuSort),
    ];

    private readonly AppSettings _settings;
    private readonly IUserInteraction _ui;
    private readonly ObservableCollection<ProcessRowViewModel> _rows = new();
    private readonly Dictionary<string, ProcessRowViewModel> _rowsByKey = new(StringComparer.Ordinal);

    private SystemSnapshot? _lastSnapshot;
    private ProcessRowViewModel? _selectedRow;
    private string _searchText = "";
    private string _summary = "";
    private bool _hasNoResults;
    private string _sortProperty = nameof(ProcessRowViewModel.MemoryBytes);
    private ListSortDirection _sortDirection = ListSortDirection.Descending;

    public ProcessListViewModel(AppSettings settings, IUserInteraction ui)
    {
        _settings = settings;
        _ui = ui;

        View = (ListCollectionView)CollectionViewSource.GetDefaultView(_rows);
        View.Filter = Matches;
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProcessRowViewModel.CategoryName)));

        View.IsLiveSorting = true;
        foreach (var property in LiveProperties)
            View.LiveSortingProperties.Add(property);

        View.IsLiveGrouping = true;
        View.LiveGroupingProperties.Add(nameof(ProcessRowViewModel.CategoryName));

        View.IsLiveFiltering = true;
        View.LiveFilteringProperties.Add(nameof(ProcessRowViewModel.CategoryOrder));

        ApplySortDescriptions();

        // Die Hinterlegungsfarben hängen vom Design ab.
        ThemeManager.Changed += (_, _) =>
        {
            foreach (var row in _rows)
                row.RefreshHeat();
        };

        KillCommand = new RelayCommand(() => _ = KillSelectedAsync(), () => SelectedRow != null);
        OpenLocationCommand = new RelayCommand(OpenLocation, () => SelectedRow?.ImagePath != null);
    }

    public ListCollectionView View { get; }

    public RelayCommand KillCommand { get; }

    public RelayCommand OpenLocationCommand { get; }

    public string SortProperty => _sortProperty;

    public ListSortDirection SortDirection => _sortDirection;

    public ProcessRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (Set(ref _selectedRow, value))
            {
                KillCommand.RaiseCanExecuteChanged();
                OpenLocationCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? ""))
                RefreshFilter();
        }
    }

    public bool ShowWindowsProcesses
    {
        get => _settings.ShowWindowsProcesses;
        set
        {
            if (_settings.ShowWindowsProcesses == value)
                return;
            _settings.ShowWindowsProcesses = value;
            _settings.Save();
            OnPropertyChanged();
            RefreshFilter();
        }
    }

    public bool GroupByName
    {
        get => _settings.GroupProcesses;
        set
        {
            if (_settings.GroupProcesses == value)
                return;
            _settings.GroupProcesses = value;
            _settings.Save();
            OnPropertyChanged();

            _rows.Clear();
            _rowsByKey.Clear();
            if (_lastSnapshot != null)
                Update(_lastSnapshot);
        }
    }

    /// <summary>z. B. „247 Prozesse · 3.410 Threads“.</summary>
    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    /// <summary>Es gibt Prozesse, aber keiner passt zu Suche/Filter.</summary>
    public bool HasNoResults
    {
        get => _hasNoResults;
        private set => Set(ref _hasNoResults, value);
    }

    public void SortBy(string property, ListSortDirection direction)
    {
        _sortProperty = property;
        _sortDirection = direction;
        ApplySortDescriptions();
    }

    private void ApplySortDescriptions()
    {
        using (View.DeferRefresh())
        {
            View.SortDescriptions.Clear();
            View.SortDescriptions.Add(new SortDescription(nameof(ProcessRowViewModel.CategoryOrder), ListSortDirection.Ascending));
            View.SortDescriptions.Add(new SortDescription(_sortProperty, _sortDirection));
            if (_sortProperty != nameof(ProcessRowViewModel.Name))
                View.SortDescriptions.Add(new SortDescription(nameof(ProcessRowViewModel.Name), ListSortDirection.Ascending));
        }
    }

    internal void Update(SystemSnapshot snapshot)
    {
        _lastSnapshot = snapshot;

        var groups = new Dictionary<string, ProcessGroup>(StringComparer.Ordinal);
        foreach (var process in snapshot.Processes)
        {
            string key = GroupByName
                ? process.ImageName.ToLowerInvariant()
                : process.Pid.ToString() + ":" + process.Identity.CreateTime.ToString();

            if (!groups.TryGetValue(key, out var group))
                groups[key] = group = new ProcessGroup();
            group.Add(process);
        }

        foreach (var pair in groups)
        {
            if (!_rowsByKey.TryGetValue(pair.Key, out var row))
            {
                row = new ProcessRowViewModel(pair.Key);
                row.Update(pair.Value, snapshot.MemoryTotalBytes);
                _rowsByKey[pair.Key] = row;
                _rows.Add(row);
            }
            else
            {
                row.Update(pair.Value, snapshot.MemoryTotalBytes);
            }
        }

        if (_rowsByKey.Count > groups.Count)
        {
            foreach (var row in _rows.Where(r => !groups.ContainsKey(r.Key)).ToList())
            {
                _rows.Remove(row);
                _rowsByKey.Remove(row.Key);
            }
        }

        Summary = $"{Format.Integer(snapshot.ProcessCount)} Prozesse · {Format.Integer(snapshot.ThreadCount)} Threads · {Format.Integer(snapshot.HandleCount)} Handles";
        HasNoResults = _rows.Count > 0 && View.IsEmpty;
        OpenLocationCommand.RaiseCanExecuteChanged();
    }

    private void RefreshFilter()
    {
        View.Refresh();
        HasNoResults = _rows.Count > 0 && View.IsEmpty;
    }

    private bool Matches(object item)
    {
        var row = (ProcessRowViewModel)item;
        if (!ShowWindowsProcesses && row.Category == ProcessCategory.Windows)
            return false;

        string query = _searchText.Trim();
        if (query.Length == 0)
            return true;

        return row.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
               row.ImageName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
               row.Members.Any(m => m.Pid.ToString() == query);
    }

    private async Task KillSelectedAsync()
    {
        var row = SelectedRow;
        if (row == null)
            return;

        if (ProcessKiller.IsCritical(row.ImageName))
        {
            _ui.ShowToast($"„{row.Name}“ ist für Windows unverzichtbar und kann nicht beendet werden.");
            return;
        }

        string message = row.Count > 1
            ? $"Alle {row.Count} Prozesse von „{row.Name}“ werden beendet."
            : $"„{row.Name}“ (PID {row.Pid}) wird beendet.";
        message += " Nicht gespeicherte Daten gehen dabei verloren.";

        if (!await _ui.ConfirmAsync("Prozess beenden?", message, "Beenden"))
            return;

        var members = row.Members.ToList();
        var errors = await Task.Run(() => members.Select(ProcessKiller.Kill).Where(e => e != ProcessKiller.Success).ToList());
        if (errors.Count == 0)
            return;

        string reason = errors.Contains(ProcessKiller.ErrorAccessDenied)
            ? "Zugriff verweigert – dafür muss SystemWatch als Administrator laufen."
            : new Win32Exception(errors[0]).Message;

        _ui.ShowToast(errors.Count == members.Count
            ? $"„{row.Name}“ konnte nicht beendet werden. {reason}"
            : $"{members.Count - errors.Count} von {members.Count} Prozessen beendet. {reason}");
    }

    private void OpenLocation()
    {
        string? path = SelectedRow?.ImagePath;
        if (path == null)
            return;

        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            _ui.ShowToast("Explorer konnte nicht geöffnet werden: " + ex.Message);
        }
    }
}
