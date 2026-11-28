using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SystemWatch.Charts;
using SystemWatch.Infrastructure;
using SystemWatch.ViewModels;

namespace SystemWatch;

public partial class MainWindow : Window, IUserInteraction
{
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(5);

    private readonly AppSettings _settings;
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _toastTimer;
    private TaskCompletionSource<bool>? _dialog;
    private WindowState _restoreState = WindowState.Normal;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        RestoreWindowBounds();

        _viewModel = new MainViewModel(_settings, this, Dispatcher);
        DataContext = _viewModel;

        _toastTimer = new DispatcherTimer { Interval = ToastDuration };
        _toastTimer.Tick += (_, _) => HideToast();

        if (Environment.GetCommandLineArgs().Contains(Autostart.MinimizedArgument, StringComparer.OrdinalIgnoreCase))
            WindowState = WindowState.Minimized;
        FrameClock.Paused = WindowState == WindowState.Minimized;

        SourceInitialized += OnSourceInitialized;
        ThemeManager.Changed += OnThemeChanged;
        StateChanged += (_, _) =>
        {
            FrameClock.Paused = WindowState == WindowState.Minimized;
            if (WindowState != WindowState.Minimized)
                _restoreState = WindowState;
        };
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            ThemeManager.Changed -= OnThemeChanged;
            _viewModel.Dispose();
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowTheming.ApplyTitleBar(this);

        // Ein zweiter Start (Verknüpfung, Strg+Umschalt+Esc) meldet sich per Fensternachricht.
        // Läuft dieses Fenster mit Adminrechten, muss die Nachricht ausdrücklich zugelassen werden.
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        source.AddHook(WndProc);
        if (SingleInstance.ActivateMessage != 0)
            Native.NativeMethods.ChangeWindowMessageFilterEx(source.Handle, SingleInstance.ActivateMessage,
                Native.NativeMethods.MsgfltAllow, IntPtr.Zero);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => WindowTheming.ApplyTitleBar(this);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (SingleInstance.ActivateMessage != 0 && (uint)msg == SingleInstance.ActivateMessage)
        {
            BringToFront();
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>Holt das Fenster nach vorn, auch aus dem minimierten Zustand.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = _restoreState;
        if (!IsVisible)
            Show();

        Activate();
        Native.NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    // ───────────── Fensterposition ─────────────

    private void RestoreWindowBounds()
    {
        Width = Math.Max(MinWidth, _settings.WindowWidth);
        Height = Math.Max(MinHeight, _settings.WindowHeight);

        if (_settings.WindowLeft is { } left && _settings.WindowTop is { } top && IsOnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (_settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
            _restoreState = WindowState.Maximized;
        }
    }

    private bool IsOnScreen(double left, double top)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return screen.IntersectsWith(new Rect(left, top, Math.Max(100, Width), 80));
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
        }

        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        _settings.Save();
    }

    // ───────────── Tastatur ─────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DialogLayer.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                CloseDialog(false);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                CloseDialog(true);
                e.Handled = true;
            }

            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.F:
                    _viewModel.CurrentPage = Page.Processes;
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(ProcessesPage.FocusSearch));
                    e.Handled = true;
                    break;
                case Key.D1:
                    _viewModel.CurrentPage = Page.Overview;
                    e.Handled = true;
                    break;
                case Key.D2:
                    _viewModel.CurrentPage = Page.Processes;
                    e.Handled = true;
                    break;
                case Key.D3:
                    _viewModel.CurrentPage = Page.Settings;
                    e.Handled = true;
                    break;
            }
        }
    }

    // ───────────── IUserInteraction ─────────────

    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        _dialog?.TrySetResult(false);
        _dialog = new TaskCompletionSource<bool>();

        DialogTitle.Text = title;
        DialogMessage.Text = message;
        DialogConfirm.Content = confirmText;
        DialogLayer.Visibility = Visibility.Visible;
        DialogLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        DialogConfirm.Focus();

        return _dialog.Task;
    }

    public void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        fade.Completed += (_, _) =>
        {
            if (Toast.Opacity == 0)
                Toast.Visibility = Visibility.Collapsed;
        };
        Toast.BeginAnimation(OpacityProperty, fade);
    }

    private void CloseDialog(bool result)
    {
        DialogLayer.Visibility = Visibility.Collapsed;
        var dialog = _dialog;
        _dialog = null;
        dialog?.TrySetResult(result);
    }

    private void DialogConfirm_Click(object sender, RoutedEventArgs e) => CloseDialog(true);

    private void DialogCancel_Click(object sender, RoutedEventArgs e) => CloseDialog(false);

    private void DialogLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CloseDialog(false);

    private void DialogCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
