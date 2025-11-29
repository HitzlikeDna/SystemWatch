using LiveCharts;
using LiveCharts.Wpf;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SystemWatch
{
    public partial class MainWindow : Window
    {
        private const string AppRegKeyPath = @"Software\SystemWatch";
        private const string RunRegKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private readonly DispatcherTimer _timer;
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _ramAvailableCounter;
        private PerformanceCounter _netSentCounter;
        private PerformanceCounter _netReceivedCounter;
        private PerformanceCounter _diskBytesCounter;
        private readonly ulong _totalRamBytes;

        private readonly ChartValues<double> _cpuValues = new ChartValues<double>();
        private readonly ChartValues<double> _ramValues = new ChartValues<double>();
        private readonly ChartValues<double> _netValues = new ChartValues<double>();
        private readonly ChartValues<double> _diskValues = new ChartValues<double>();
        private int _historyLength = 60;
        private string _currentDrive = "C:\\";

        public MainWindow()
        {
            InitializeComponent();

            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ = _cpuCounter.NextValue();

            _ramAvailableCounter = new PerformanceCounter("Memory", "Available MBytes");

            _totalRamBytes = GetTotalMemoryInBytes();

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
            var category = new PerformanceCounterCategory("Network Interface");
            string[] instances = category.GetInstanceNames();

            var filtered = instances
                .Where(name =>
                    !name.ToLower().Contains("loopback") &&
                    !name.ToLower().Contains("isatap"))
                .ToArray();

            AdapterCombo.Items.Clear();

            foreach (var name in filtered)
            {
                var item = new ComboBoxItem { Content = name, Tag = name };
                AdapterCombo.Items.Add(item);
            }

            if (AdapterCombo.Items.Count == 0)
            {
                foreach (var name in instances)
                {
                    var item = new ComboBoxItem { Content = name, Tag = name };
                    AdapterCombo.Items.Add(item);
                }
            }

            if (AdapterCombo.Items.Count > 0)
                AdapterCombo.SelectedIndex = 0;

            InitDiskCounter();
        }

        private void InitDiskCounter()
        {
            try
            {
                var category = new PerformanceCounterCategory("PhysicalDisk");
                string[] instances = category.GetInstanceNames();
                string name = instances.FirstOrDefault(n => n == "_Total") ?? instances.FirstOrDefault() ?? "";
                if (!string.IsNullOrEmpty(name))
                {
                    _diskBytesCounter = new PerformanceCounter("PhysicalDisk", "Disk Bytes/sec", name);
                    _ = _diskBytesCounter.NextValue();
                }
            }
            catch
            {
                _diskBytesCounter = null;
            }
        }

        private void InitDrives()
        {
            DriveCombo.Items.Clear();

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                {
                    var item = new ComboBoxItem { Content = drive.Name, Tag = drive.Name };
                    DriveCombo.Items.Add(item);
                }
            }

            if (DriveCombo.Items.Count > 0)
                DriveCombo.SelectedIndex = 0;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            float cpuUsage = _cpuCounter.NextValue();

            float availableMB = _ramAvailableCounter.NextValue();
            double availableBytes = availableMB * 1024 * 1024;
            double usedPercent = (1 - (availableBytes / _totalRamBytes)) * 100.0;

            CpuText.Text = $"{cpuUsage:0.0} %";
            RamText.Text = $"{usedPercent:0.0} %";

            double kBps = 0;
            if (_netSentCounter != null && _netReceivedCounter != null)
            {
                float sent = _netSentCounter.NextValue();
                float received = _netReceivedCounter.NextValue();
                double totalBytesPerSec = sent + received;
                kBps = totalBytesPerSec / 1024.0;
                NetText.Text = $"{kBps:0.0} kB/s";
            }
            else
            {
                NetText.Text = "kein Adapter";
            }

            double diskMBps = 0;
            if (_diskBytesCounter != null)
            {
                double bytes = _diskBytesCounter.NextValue();
                diskMBps = bytes / (1024.0 * 1024.0);
            }

            try
            {
                var drive = new DriveInfo(_currentDrive);
                if (drive.IsReady)
                {
                    double totalGB = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freeGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double usedGB = totalGB - freeGB;
                    double usedDiskPercent = (usedGB / totalGB) * 100.0;
                    DiskText.Text = $"{_currentDrive} {freeGB:0.0} GB frei / {totalGB:0.0} GB ({usedDiskPercent:0.0} % genutzt), IO {diskMBps:0.00} MB/s";
                }
                else
                {
                    DiskText.Text = $"{_currentDrive} nicht bereit, IO {diskMBps:0.00} MB/s";
                }
            }
            catch
            {
                DiskText.Text = $"{_currentDrive} Fehler, IO {diskMBps:0.00} MB/s";
            }

            AppendValue(_cpuValues, cpuUsage);
            AppendValue(_ramValues, usedPercent);
            AppendValue(_netValues, kBps);
            AppendValue(_diskValues, diskMBps);
        }

        private void AppendValue(ChartValues<double> values, double newValue)
        {
            if (values.Count >= _historyLength)
                values.RemoveAt(0);
            values.Add(newValue);
        }

        private static ulong GetTotalMemoryInBytes()
        {
            MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
                return mem.ullTotalPhys;
            return 0;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

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
                try
                {
                    _netSentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", name);
                    _netReceivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", name);
                    _ = _netSentCounter.NextValue();
                    _ = _netReceivedCounter.NextValue();
                }
                catch
                {
                    _netSentCounter = null;
                    _netReceivedCounter = null;
                }
            }
        }

        private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DriveCombo.SelectedItem is ComboBoxItem item &&
                item.Tag is string name &&
                !string.IsNullOrWhiteSpace(name))
            {
                _currentDrive = name;
            }
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CpuChart == null || RamChart == null || NetChart == null || DiskChart == null)
                return;

            if (ThemeCombo.SelectedItem is ComboBoxItem item &&
                item.Tag is string mode)
            {
                if (mode == "Dark")
                {
                    Background = Brushes.Black;
                    Foreground = Brushes.White;
                    CpuChart.Background = Brushes.Black;
                    RamChart.Background = Brushes.Black;
                    NetChart.Background = Brushes.Black;
                    DiskChart.Background = Brushes.Black;
                }
                else
                {
                    Background = SystemColors.WindowBrush;
                    Foreground = SystemColors.ControlTextBrush;
                    CpuChart.Background = SystemColors.WindowBrush;
                    RamChart.Background = SystemColors.WindowBrush;
                    NetChart.Background = SystemColors.WindowBrush;
                    DiskChart.Background = SystemColors.WindowBrush;
                }
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
