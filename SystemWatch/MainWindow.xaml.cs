using LiveCharts;
using LiveCharts.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SystemWatch.Monitoring;

namespace SystemWatch
{
    public partial class MainWindow : Window
    {
        private const string AppRegKeyPath = @"Software\SystemWatch";
        private const string RunRegKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private readonly DispatcherTimer _timer;
        private readonly SystemMonitor _monitor;
        private readonly ChartValues<double> _cpuValues = new ChartValues<double>();
        private readonly ChartValues<double> _ramValues = new ChartValues<double>();
        private readonly ChartValues<double> _gpuValues = new ChartValues<double>();
        private readonly ChartValues<double> _netValues = new ChartValues<double>();
        private readonly ChartValues<double> _diskValues = new ChartValues<double>();
        private int _historyLength = 60;
        public ObservableCollection<ProcessInfo> Processes { get; set; } = new ObservableCollection<ProcessInfo>();

        private string[] _systemExclusions = { "idle", "system", "svchost", "runtimebroker", "csrss", "wininit", "services", "lsass", "smss", "winlogon", "fontdrvhost", "dwm" };

        public MainWindow()
        {
            InitializeComponent();
            _monitor = new SystemMonitor();
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(1000);
            _timer.Tick += Timer_Tick;
            ProcessListView.ItemsSource = Processes;
        }

        private void InitCpuChart() { SetupChart(CpuChart, _cpuValues, "CPU %", 100); }
        private void InitRamChart() { SetupChart(RamChart, _ramValues, "RAM %", 100); }
        private void InitGpuChart() { SetupChart(GpuChart, _gpuValues, "GPU %", 100); }
        private void InitNetChart() { SetupChart(NetChart, _netValues, "NET", 0); }
        private void InitDiskChart() { SetupChart(DiskChart, _diskValues, "DISK", 0); }

        private void SetupChart(CartesianChart chart, ChartValues<double> values, string title, double max)
        {
            if (chart == null) return;
            values.Clear();
            for (int i = 0; i < _historyLength; i++) values.Add(0);
            chart.Series = new SeriesCollection { new LineSeries { Values = values, PointGeometry = null, LineSmoothness = 0, Fill = Brushes.Transparent, StrokeThickness = 2 } };
            chart.AxisX = new AxesCollection { new Axis { ShowLabels = false } };
            if (max > 0) chart.AxisY = new AxesCollection { new Axis { MinValue = 0, MaxValue = max, ShowLabels = false } };
            else chart.AxisY = new AxesCollection { new Axis { MinValue = 0, ShowLabels = false } };
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            var stats = _monitor.Read();
            CpuText.Text = $"{stats.CpuPercent:0}%";
            RamText.Text = $"{stats.RamPercent:0}%";
            GpuText.Text = stats.GpuAvailable ? $"{stats.GpuPercent:0}%" : "N/A";
            NetText.Text = $"{stats.NetKiloBytesPerSecond:0} kB/s";
            DiskText.Text = $"{stats.DriveName} {stats.DriveUsedPercent:0}%";

            AppendValue(_cpuValues, stats.CpuPercent);
            AppendValue(_ramValues, stats.RamPercent);
            AppendValue(_gpuValues, stats.GpuPercent ?? 0);
            AppendValue(_netValues, stats.NetKiloBytesPerSecond);
            AppendValue(_diskValues, stats.DiskMegaBytesPerSecond);

            UpdateProcessList();
        }

        private void UpdateProcessList()
        {
            var running = Process.GetProcesses()
                .Where(p => !_systemExclusions.Contains(p.ProcessName.ToLower()))
                .Select(p => new ProcessInfo { Name = p.ProcessName, Id = p.Id, MemoryMb = p.WorkingSet64 / 1024.0 / 1024.0 })
                .OrderByDescending(p => p.MemoryMb)
                .Take(30)
                .ToList();

            if (running.Count != Processes.Count || (Processes.Count > 0 && running[0].MemoryMb != Processes[0].MemoryMb))
            {
                Processes.Clear();
                foreach (var p in running) Processes.Add(p);
            }
        }

        private void KillProcess_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                try { Process.GetProcessById(id).Kill(); }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
            }
        }

        private void AppendValue(ChartValues<double> values, double newValue)
        {
            values.RemoveAt(0);
            values.Add(newValue);
        }

        private void UpdateRateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_timer != null && UpdateRateCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag.ToString(), out int ms))
                _timer.Interval = TimeSpan.FromMilliseconds(ms);
        }

        private void HistoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HistoryCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag.ToString(), out int len))
            {
                _historyLength = len;
                InitCpuChart(); InitRamChart(); InitGpuChart(); InitNetChart(); InitDiskChart();
            }
        }

        private void AdapterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AdapterCombo.SelectedItem is ComboBoxItem item) _monitor.SetNetworkAdapter(item.Tag.ToString());
        }

        private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DriveCombo.SelectedItem is ComboBoxItem item) _monitor.SetDrive(item.Tag.ToString());
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CpuChart == null) return;
            var item = ThemeCombo.SelectedItem as ComboBoxItem;
            var windowBg = (SolidColorBrush)Resources["WindowBackgroundBrush"];
            var cardBg = (SolidColorBrush)Resources["CardBackgroundBrush"];
            var fg = (SolidColorBrush)Resources["ForegroundBrush"];
            if (item.Tag.ToString() == "Dark") { windowBg.Color = Color.FromRgb(30, 30, 30); cardBg.Color = Color.FromRgb(45, 45, 45); fg.Color = Colors.White; }
            else { windowBg.Color = Color.FromRgb(240, 242, 245); cardBg.Color = Colors.White; fg.Color = Colors.Black; }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var key = Registry.CurrentUser.OpenSubKey(AppRegKeyPath);
            if (key != null)
            {
                if (double.TryParse(key.GetValue("Width")?.ToString(), out double dw)) Width = dw;
                if (double.TryParse(key.GetValue("Height")?.ToString(), out double dh)) Height = dh;
            }
            InitCpuChart(); InitRamChart(); InitGpuChart(); InitNetChart(); InitDiskChart();
            InitAdapters(); InitDrives();
            _timer.Start();
        }

        private void InitAdapters()
        {
            foreach (var a in _monitor.GetNetworkAdapters()) AdapterCombo.Items.Add(new ComboBoxItem { Content = a, Tag = a });
            if (AdapterCombo.Items.Count > 0) AdapterCombo.SelectedIndex = 0;
        }

        private void InitDrives()
        {
            foreach (var d in _monitor.GetDrives()) DriveCombo.Items.Add(new ComboBoxItem { Content = d, Tag = d });
            if (DriveCombo.Items.Count > 0) DriveCombo.SelectedIndex = 0;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var key = Registry.CurrentUser.CreateSubKey(AppRegKeyPath);
            key.SetValue("Width", Width); key.SetValue("Height", Height);
            _monitor.Dispose();
        }

        private void AutostartCheck_Changed(object sender, RoutedEventArgs e)
        {
            var key = Registry.CurrentUser.CreateSubKey(RunRegKeyPath);
            if (AutostartCheck.IsChecked == true) key.SetValue("SystemWatch", "\"" + Assembly.GetExecutingAssembly().Location + "\"");
            else key.DeleteValue("SystemWatch", false);
        }
    }

    public class ProcessInfo
    {
        public string Name { get; set; }
        public int Id { get; set; }
        public double MemoryMb { get; set; }
    }
}