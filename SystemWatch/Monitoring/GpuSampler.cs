using System;
using System.Collections.Generic;

namespace SystemWatch.Monitoring;

/// <summary>
/// GPU-Auslastung wie im Task-Manager: Pro Engine (3D, Copy, Video …) werden die Anteile aller
/// Prozesse addiert, angezeigt wird die am stärksten ausgelastete Engine. Nebenbei fällt die
/// GPU-Last je Prozess ab.
/// </summary>
internal sealed class GpuSampler
{
    private readonly CounterCategoryReader _engines = new("GPU Engine", "utilization percentage");
    private readonly CounterCategoryReader _memory = new("GPU Adapter Memory");
    private readonly Dictionary<string, double> _perEngine = new(StringComparer.Ordinal);

    public bool IsAvailable => _engines.IsAvailable;

    /// <summary>Liest die aktuelle Auslastung. <paramref name="perProcess"/> erhält die GPU-Last je PID.</summary>
    public double? Sample(Dictionary<int, double> perProcess, out ulong? dedicatedUsedBytes)
    {
        dedicatedUsedBytes = _memory.SumRaw("dedicated usage");
        perProcess.Clear();
        _perEngine.Clear();

        bool ok = _engines.Read((_, name, value) =>
        {
            if (value <= 0)
                return;

            // Format: pid_1234_luid_0x…_0x…_phys_0_eng_3_engtype_3D
            int luidIndex = name.IndexOf("_luid_", StringComparison.Ordinal);
            if (luidIndex < 0)
                return;

            string engine = name.Substring(luidIndex + 1);
            _perEngine.TryGetValue(engine, out double engineSum);
            _perEngine[engine] = engineSum + value;

            if (TryParsePid(name, luidIndex, out int pid))
            {
                perProcess.TryGetValue(pid, out double processMax);
                if (value > processMax)
                    perProcess[pid] = Math.Min(100, value);
            }
        });

        if (!ok)
            return null;

        double total = 0;
        foreach (var value in _perEngine.Values)
            total = Math.Max(total, value);
        return Math.Min(100, total);
    }

    private static bool TryParsePid(string name, int luidIndex, out int pid)
    {
        pid = 0;
        const string prefix = "pid_";
        return name.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(name.Substring(prefix.Length, luidIndex - prefix.Length), out pid);
    }
}
