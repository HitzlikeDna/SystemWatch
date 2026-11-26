using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Threading;

namespace SystemWatch.Charts;

/// <summary>Etwas, das vom gemeinsamen Bildtakt animiert wird.</summary>
internal interface IFrameTarget
{
    void RenderFrame(long now);
}

/// <summary>
/// Gemeinsamer Bildtakt für alle Animationen (Graphen, Balken).
/// <para>
/// Bewusst ein Timer statt <c>CompositionTarget.Rendering</c>: Letzteres hält WPF dauerhaft im
/// 60-Hz-Rendermodus, auch wenn sich nichts ändert. Mit dem Timer entscheidet jedes Element
/// selbst, ob sich ein neues Bild lohnt – WPF rendert nur dann. Läuft nur, solange es
/// tatsächlich Abonnenten gibt; minimiert oder ausgeschaltet kostet er nichts.
/// </para>
/// </summary>
internal static class FrameClock
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(15);
    private static readonly List<IFrameTarget> Subscribers = new();

    private static DispatcherTimer? _timer;
    private static bool _enabled = true;
    private static bool _paused;

    public static event EventHandler? EnabledChanged;

    /// <summary>Flüssige Animationen an/aus (Einstellung).</summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            UpdateTimer();
            EnabledChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Pausiert alle Animationen, z. B. wenn das Fenster minimiert ist.</summary>
    public static bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value)
                return;
            _paused = value;
            UpdateTimer();
        }
    }

    public static void Register(IFrameTarget target)
    {
        if (!Subscribers.Contains(target))
            Subscribers.Add(target);
        UpdateTimer();
    }

    public static void Unregister(IFrameTarget target)
    {
        Subscribers.Remove(target);
        UpdateTimer();
    }

    private static void UpdateTimer()
    {
        bool shouldRun = _enabled && !_paused && Subscribers.Count > 0;
        if (shouldRun)
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TickInterval };
                _timer.Tick += OnTick;
            }

            if (!_timer.IsEnabled)
                _timer.Start();
        }
        else
        {
            _timer?.Stop();
        }
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();

        // Kopie, falls sich ein Element während des Zeichnens abmeldet.
        foreach (var target in Subscribers.ToArray())
            target.RenderFrame(now);
    }
}
