using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SystemWatch.Monitoring;

/// <summary>
/// Liest alle Instanzen einer Leistungsindikator-Kategorie auf einmal und berechnet die Werte aus
/// zwei aufeinanderfolgenden Messungen. Praktisch für Kategorien, deren Instanzen sich ändern
/// (GPU-Engines je Prozess) oder bei denen alle Instanzen gebraucht werden (jedes Laufwerk).
/// </summary>
internal sealed class CounterCategoryReader
{
    private readonly PerformanceCounterCategory? _category;
    private readonly string[] _counters;
    private Dictionary<string, CounterSample>[] _previous;

    public CounterCategoryReader(string category, params string[] counters)
    {
        _counters = counters;
        _previous = new Dictionary<string, CounterSample>[counters.Length];
        for (int i = 0; i < counters.Length; i++)
            _previous[i] = new Dictionary<string, CounterSample>(StringComparer.Ordinal);

        try
        {
            if (PerformanceCounterCategory.Exists(category))
                _category = new PerformanceCounterCategory(category);
        }
        catch
        {
            _category = null;
        }
    }

    public bool IsAvailable => _category != null;

    /// <summary>
    /// Liest die Kategorie neu. <paramref name="onValue"/> erhält (Zählerindex, Instanz, berechneter Wert)
    /// für jede Instanz, die schon in der vorigen Messung existierte.
    /// </summary>
    public bool Read(Action<int, string, double> onValue)
    {
        if (_category == null)
            return false;

        InstanceDataCollectionCollection data;
        try
        {
            data = _category.ReadCategory();
        }
        catch
        {
            return false;
        }

        for (int i = 0; i < _counters.Length; i++)
        {
            var samples = data[_counters[i]];
            if (samples == null)
                continue;

            var previous = _previous[i];
            var current = new Dictionary<string, CounterSample>(samples.Count, StringComparer.Ordinal);

            foreach (InstanceData instance in samples.Values)
            {
                var sample = instance.Sample;
                current[instance.InstanceName] = sample;

                if (!previous.TryGetValue(instance.InstanceName, out var before))
                    continue;

                double value;
                try
                {
                    value = CounterSample.Calculate(before, sample);
                }
                catch
                {
                    continue;
                }

                if (!double.IsNaN(value) && !double.IsInfinity(value))
                    onValue(i, instance.InstanceName, value);
            }

            _previous[i] = current;
        }

        return true;
    }

    /// <summary>Summe der Rohwerte eines Zählers über alle Instanzen (für absolute Werte wie Bytes).</summary>
    public ulong? SumRaw(string counter)
    {
        if (_category == null)
            return null;

        try
        {
            var samples = _category.ReadCategory()[counter];
            if (samples == null)
                return null;

            ulong sum = 0;
            foreach (InstanceData instance in samples.Values)
                sum += (ulong)Math.Max(0, instance.RawValue);
            return sum;
        }
        catch
        {
            return null;
        }
    }
}
