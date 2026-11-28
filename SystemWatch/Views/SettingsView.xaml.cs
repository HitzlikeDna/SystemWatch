using System.Windows;
using System.Windows.Controls;
using SystemWatch.ViewModels;

namespace SystemWatch.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // Der Task-Manager-Eintrag kann sich auch außerhalb ändern – beim Öffnen neu lesen.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                (DataContext as SettingsViewModel)?.RefreshReplacement();
        };
    }

    private void RefreshAdapters_Click(object sender, RoutedEventArgs e) =>
        (DataContext as SettingsViewModel)?.RefreshAdapters();
}
