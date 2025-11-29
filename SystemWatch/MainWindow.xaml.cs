using LiveCharts;
using LiveCharts.Wpf;
using Microsoft.Win32;
using System;
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


        public MainWindow()
        {
            InitializeComponent();

            _monitor = new SystemMonitor();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(1000);
            _timer.Tick += Timer_Tick;
        }

        private void InitCpuChart()
        {
            if (CpuChart == null)
                return;

            _cpuValues.Clear();
            for (int i = 0; i < _historyLength; i++)
                _cpuValues.Add(0);

            CpuChart.Series = new SeriesCollection
            {
                new LineSeries
                {
                    Title = "CPU %",
                    Values = _cpuValues,
                    PointGeometry = null,
                    LineSmoothness = 0
                }
            };

            CpuChart.AxisY = new AxesCollection
            {
                new Axis { MinValue = 0, MaxValue = 100, Title = "CPU %" }
            };
        }

        private void InitRamChart()
        {
            if (RamChart == null)
                return;

            _ramValues.Clear();
            for (int i = 0; i < _historyLength; i++)
                _ramValues.Add(0);

            RamChart.Series = new SeriesCollection
            {
                new LineSeries
                {
                    Title = "RAM %",
                    Values = _ramValues,
                    PointGeometry = null,
                    LineSmoothness = 0
                }
            };

            RamChart.AxisY = new AxesCollection
            {
                new Axis { MinValue = 0, MaxValue = 100, Title = "RAM %" }
            };
        }

        private void InitGpuChart()
        {
            if (GpuChart == null)
                return;

            _gpuValues.Clear();
            for (int i = 0; i < _historyLength; i++)
                _gpuValues.Add(0);

            GpuChart.Series = new SeriesCollection
            {
                new LineSeries
                {
                    Title = "GPU %",
                    Values = _gpuValues,
                    PointGeometry = null,
                    LineSmoothness = 0
                }
            };

            GpuChart.AxisY = new AxesCollection
            {
                new Axis { MinValue = 0, MaxValue = 100, Title = "GPU %" }
            };
        }

        private void InitNetChart()
        {
            if (NetChart == null)
                return;

            _netValues.Clear();
            for (int i = 0; i < _historyLength; i++)
                _netValues.Add(0);

            NetChart.Series = new SeriesCollection
            {
                new LineSeries
                {
                    Title = "Netz kB/s",
                    Values = _netValues,
                    PointGeometry = null,
                    LineSmoothness = 0
                }
            };
        }

        private void InitDiskChart()
        {
            if (DiskChart == null)
                return;

            _diskValues.Clear();
            for (int i = 0; i < _historyLength; i++)
                _diskValues.Add(0);

            DiskChart.Series = new SeriesCollection
            {
                new LineSeries
                {
                    Title = "Disk MB/s",
                    Values = _diskValues,
                    PointGeometry = null,
                    LineSmoothness = 0
                }
            };
        }

        private void InitAdapters()
        {
            var adapters = _monitor.GetNetworkAdapters();

            AdapterCombo.Items.Clear();

            foreach (var name in adapters)
            {
                var item = new ComboBoxItem { Content = name, Tag = name };
                AdapterCombo.Items.Add(item);
            }

            if (AdapterCombo.Items.Count > 0)
                AdapterCombo.SelectedIndex = 0;
        }

        private void InitDrives()
        {
            var drives = _monitor.GetDrives();

            DriveCombo.Items.Clear();

            foreach (var name in drives)
            {
                var item = new ComboBoxItem { Content = name, Tag = name };
                DriveCombo.Items.Add(item);
            }

            if (DriveCombo.Items.Count > 0)
                DriveCombo.SelectedIndex = 0;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            var stats = _monitor.Read();

            CpuText.Text = $"{stats.CpuPercent:0.0} %";
            RamText.Text = $"{stats.RamPercent:0.0} %";

            if (stats.GpuAvailable)
                GpuText.Text = $"{stats.GpuPercent:0.0} %";
            else
                GpuText.Text = "nicht verfügbar";

            if (stats.NetworkAdapterAvailable)
                NetText.Text = $"{stats.NetKiloBytesPerSecond:0.0} kB/s";
            else
                NetText.Text = "kein Adapter";

            if (stats.DriveError)
            {
                DiskText.Text = $"{stats.DriveName} Fehler, IO {stats.DiskMegaBytesPerSecond:0.00} MB/s";
            }
            else if (!stats.DriveReady)
            {
                DiskText.Text = $"{stats.DriveName} nicht bereit, IO {stats.DiskMegaBytesPerSecond:0.00} MB/s";
            }
            else
            {
                DiskText.Text =
                    $"{stats.DriveName} {stats.DriveFreeGb:0.0} GB frei / {stats.DriveTotalGb:0.0} GB ({stats.DriveUsedPercent:0.0} % genutzt), IO {stats.DiskMegaBytesPerSecond:0.00} MB/s";
            }

            AppendValue(_cpuValues, stats.CpuPercent);
            AppendValue(_ramValues, stats.RamPercent);
            AppendValue(_gpuValues, stats.GpuPercent ?? 0);
            AppendValue(_netValues, stats.NetKiloBytesPerSecond);
            AppendValue(_diskValues, stats.DiskMegaBytesPerSecond);
        }

        private void AppendValue(ChartValues<double> values, double newValue)
        {
            if (values.Count >= _historyLength)
                values.RemoveAt(0);
            values.Add(newValue);
        }

        private void UpdateRateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_timer == null)
                return;

            if (UpdateRateCombo.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag.ToString(), out int ms))
            {
                _timer.Interval = TimeSpan.FromMilliseconds(ms);
            }
        }

        private void HistoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HistoryCombo.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag.ToString(), out int len))
            {
                _historyLength = len;
                InitCpuChart();
                InitRamChart();
                InitGpuChart();
                InitNetChart();
                InitDiskChart();
            }
        }

        private void AdapterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AdapterCombo.SelectedItem is ComboBoxItem item &&
                item.Tag is string name &&
                !string.IsNullOrWhiteSpace(name))
            {
                _monitor.SetNetworkAdapter(name);
            }
        }

        private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DriveCombo.SelectedItem is ComboBoxItem item &&
                item.Tag is string name &&
                !string.IsNullOrWhiteSpace(name))
            {
                _monitor.SetDrive(name);
            }
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CpuChart == null || RamChart == null || GpuChart == null || NetChart == null || DiskChart == null)
                return;

            if (ThemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string mode)
            {
                var windowBg = (SolidColorBrush)Resources["WindowBackgroundBrush"];
                var cardBg = (SolidColorBrush)Resources["CardBackgroundBrush"];
                var fg = (SolidColorBrush)Resources["ForegroundBrush"];

                if (mode == "Dark")
                {
                    windowBg.Color = Color.FromRgb(36, 39, 46);   // dunkles Grau
                    cardBg.Color = Color.FromRgb(45, 48, 56);     // Karten-Grau
                    fg.Color = Colors.White;
                }
                else
                {
                    windowBg.Color = Color.FromRgb(240, 242, 245); // helles Grau
                    cardBg.Color = Colors.White;
                    fg.Color = Color.FromRgb(0, 0, 0);
                }

                CpuChart.Background = cardBg;
                RamChart.Background = cardBg;
                GpuChart.Background = cardBg;
                NetChart.Background = cardBg;
                DiskChart.Background = cardBg;
            }
        }



        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(AppRegKeyPath))
            {
                if (key != null)
                {
                    object w = key.GetValue("Width");
                    object h = key.GetValue("Height");
                    object l = key.GetValue("Left");
                    object t = key.GetValue("Top");
                    object s = key.GetValue("WindowState");

                    if (w != null && h != null && l != null && t != null)
                    {
                        if (double.TryParse(w.ToString(), out double dw)) Width = dw;
                        if (double.TryParse(h.ToString(), out double dh)) Height = dh;
                        if (double.TryParse(l.ToString(), out double dl)) Left = dl;
                        if (double.TryParse(t.ToString(), out double dt)) Top = dt;
                    }

                    if (s is string state)
                    {
                        if (state == WindowState.Maximized.ToString())
                            WindowState = WindowState.Maximized;
                        else if (state == WindowState.Normal.ToString())
                            WindowState = WindowState.Normal;
                    }
                }
                ThemeCombo.SelectedIndex = 1;
            }

            using (var runKey = Registry.CurrentUser.OpenSubKey(RunRegKeyPath))
            {
                if (runKey != null)
                {
                    object value = runKey.GetValue("SystemWatch");
                    AutostartCheck.IsChecked = value != null;
                }
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                InitCpuChart();
                InitRamChart();
                InitGpuChart();
                InitNetChart();
                InitDiskChart();
                InitAdapters();
                InitDrives();
                _timer.Start();
            }), DispatcherPriority.Loaded);
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(AppRegKeyPath))
            {
                if (WindowState == WindowState.Normal)
                {
                    key.SetValue("Width", Width);
                    key.SetValue("Height", Height);
                    key.SetValue("Left", Left);
                    key.SetValue("Top", Top);
                }
                else
                {
                    key.SetValue("Width", RestoreBounds.Width);
                    key.SetValue("Height", RestoreBounds.Height);
                    key.SetValue("Left", RestoreBounds.Left);
                    key.SetValue("Top", RestoreBounds.Top);
                }

                key.SetValue("WindowState", WindowState.ToString());
            }

            _monitor.Dispose();
        }

        private void AutostartCheck_Changed(object sender, RoutedEventArgs e)
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            using (var key = Registry.CurrentUser.CreateSubKey(RunRegKeyPath))
            {
                if (AutostartCheck.IsChecked == true)
                    key.SetValue("SystemWatch", "\"" + exePath + "\"");
                else
                    key.DeleteValue("SystemWatch", false);
            }
        }
    }
}
