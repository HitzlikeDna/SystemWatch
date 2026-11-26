using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SystemWatch.Monitoring;

/// <summary>
/// Misst alle Werte auf einem eigenen Thread in festem Takt. Die Oberfläche bleibt dadurch
/// frei für flüssige Animationen und bekommt fertige <see cref="SystemSnapshot"/>s geliefert.
/// </summary>
public sealed class MonitorService : IDisposable
{
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _stopping;
    private volatile int _intervalMs;
    private volatile bool _intervalChanged;
    private volatile string _networkAdapterId;

    public MonitorService(int intervalMs, string networkAdapterId)
    {
        _intervalMs = intervalMs;
        _networkAdapterId = networkAdapterId ?? "";
        _thread = new Thread(Run) { IsBackground = true, Name = "SystemWatch-Messung" };
    }

    /// <summary>Wird auf dem Mess-Thread ausgelöst.</summary>
    public event Action<SystemSnapshot>? SnapshotReady;

    public int IntervalMs
    {
        get => _intervalMs;
        set
        {
            if (_intervalMs == value)
                return;
            _intervalMs = value;
            _intervalChanged = true;
            _wake.Set();
        }
    }

    public string NetworkAdapterId
    {
        get => _networkAdapterId;
        set => _networkAdapterId = value ?? "";
    }

    public void Start() => _thread.Start();

    private void Run()
    {
        var hardware = HardwareInfoReader.Read();
        using var system = new SystemSampler();
        using var processes = new ProcessSampler();
        var gpu = new GpuSampler();
        var network = new NetworkSampler();
        var gpuByPid = new Dictionary<int, double>();

        long next = Stopwatch.GetTimestamp();

        while (!_stopping)
        {
            var snapshot = new SystemSnapshot
            {
                Hardware = hardware,
                IntervalSeconds = _intervalMs / 1000.0,
            };

            Try(() => system.Sample(snapshot));
            Try(() =>
            {
                snapshot.GpuPercent = gpu.Sample(gpuByPid, out var dedicated);
                snapshot.GpuDedicatedUsedBytes = dedicated;
            });
            Try(() =>
            {
                network.SelectedAdapterId = _networkAdapterId;
                network.Sample(snapshot);
            });
            Try(() => processes.Sample(snapshot, gpuByPid, gpu.IsAvailable));

            snapshot.Timestamp = Stopwatch.GetTimestamp();
            if (!_stopping)
                SnapshotReady?.Invoke(snapshot);

            // Fester Takt: Die Messdauer wird vom Warten abgezogen.
            long interval = _intervalMs * Stopwatch.Frequency / 1000;
            long now = Stopwatch.GetTimestamp();
            next += interval;
            if (next < now || _intervalChanged)
                next = now + interval;
            _intervalChanged = false;

            int waitMs = (int)((next - now) * 1000 / Stopwatch.Frequency);
            if (waitMs > 0 && _wake.WaitOne(waitMs) && _intervalChanged)
                next = Stopwatch.GetTimestamp();
        }
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Messung fehlgeschlagen: " + ex);
        }
    }

    public void Dispose()
    {
        _stopping = true;
        _wake.Set();

        // Hängt eine Messung (z. B. Treiber), lässt der Hintergrund-Thread das Beenden trotzdem zu.
        if (!_thread.IsAlive || _thread.Join(TimeSpan.FromSeconds(2)))
            _wake.Dispose();
    }
}
