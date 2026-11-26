using System.Windows;
using System.Windows.Controls;
using SystemWatch.ViewModels;

namespace SystemWatch.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void RefreshAdapters_Click(object sender, RoutedEventArgs e) =>
        (DataContext as SettingsViewModel)?.RefreshAdapters();
}
