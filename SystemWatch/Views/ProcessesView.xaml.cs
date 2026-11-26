using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SystemWatch.ViewModels;

namespace SystemWatch.Views;

public partial class ProcessesView : UserControl
{
    private ProcessListViewModel? _viewModel;

    public ProcessesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(StretchNameColumn));
        };
        Grid.SizeChanged += (_, _) => StretchNameColumn();
    }

    /// <summary>
    /// Das DataGrid verteilt Stern-Breiten nur beim ersten Layout. War die Seite da noch
    /// ausgeblendet, bliebe die Name-Spalte schmal – deshalb hier neu anstoßen.
    /// </summary>
    private void StretchNameColumn()
    {
        NameColumn.Width = new DataGridLength(NameColumn.MinWidth);
        NameColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as ProcessListViewModel;
        if (_viewModel == null)
            return;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdatePidColumn();
        ShowSortIndicator(_viewModel.SortProperty, _viewModel.SortDirection);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProcessListViewModel.GroupByName))
            UpdatePidColumn();
    }

    /// <summary>In der zusammengefassten Ansicht hat eine Zeile mehrere PIDs – Spalte ausblenden.</summary>
    private void UpdatePidColumn() =>
        PidColumn.Visibility = _viewModel?.GroupByName == true ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Eigene Sortierung: Die Gruppen (Apps, Hintergrund, Windows) bleiben immer in fester
    /// Reihenfolge, sortiert wird innerhalb der Gruppen. Zahlen zuerst absteigend.
    /// </summary>
    private void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (_viewModel == null)
            return;

        string property = e.Column.SortMemberPath;
        bool isText = property == nameof(ProcessRowViewModel.Name);
        var direction = e.Column.SortDirection switch
        {
            ListSortDirection.Descending => ListSortDirection.Ascending,
            ListSortDirection.Ascending => ListSortDirection.Descending,
            _ => isText ? ListSortDirection.Ascending : ListSortDirection.Descending,
        };

        _viewModel.SortBy(property, direction);
        ShowSortIndicator(property, direction);
    }

    private void ShowSortIndicator(string property, ListSortDirection direction)
    {
        foreach (var column in Grid.Columns)
            column.SortDirection = column.SortMemberPath == property ? direction : null;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SearchBox.Text.Length > 0)
        {
            SearchBox.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            Grid.Focus();
            if (Grid.SelectedIndex < 0 && Grid.Items.Count > 0)
                Grid.SelectedIndex = 0;
            e.Handled = true;
        }
    }
}
