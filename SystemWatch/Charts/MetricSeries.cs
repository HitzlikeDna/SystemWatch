using System;

namespace SystemWatch.Charts;

/// <summary>
/// Ringpuffer mit den letzten Messwerten einer Kennzahl. <see cref="Capacity"/> Punkte füllen die
/// Breite des Graphen; zwei weitere werden vorgehalten, damit beim Gleiten links keine Lücke entsteht.
/// </summary>
public sealed class MetricSeries
{
    private const int Overscan = 2;

    private double[] _values;
    private int _head;
    private int _count;

    public MetricSeries(int capacity)
    {
        Capacity = Math.Max(2, capacity);
        _values = new double[Capacity + Overscan];
    }

    public event EventHandler? Changed;

    /// <summary>Anzahl der Punkte, die die volle Breite des Graphen abdecken.</summary>
    public int Capacity { get; private set; }

    public int Count => _count;

    /// <summary>Zeitpunkt des letzten Werts (Stopwatch-Ticks).</summary>
    public long LastTimestamp { get; private set; }

    /// <summary>Abstand zwischen zwei Werten in Sekunden.</summary>
    public double IntervalSeconds { get; private set; } = 1;

    public double Latest => _count == 0 ? 0 : GetByAge(0);

    /// <summary>Wert nach Alter: 0 = neuester Wert.</summary>
    public double GetByAge(int age)
    {
        int index = _head - 1 - age;
        if (index < 0)
            index += _values.Length;
        return _values[index];
    }

    public void Push(double value, long timestamp, double intervalSeconds)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            value = 0;

        _values[_head] = value;
        _head = (_head + 1) % _values.Length;
        if (_count < _values.Length)
            _count++;

        LastTimestamp = timestamp;
        IntervalSeconds = intervalSeconds > 0 ? intervalSeconds : 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ändert die Länge des Verlaufs und behält die neuesten Werte.</summary>
    public void Resize(int capacity)
    {
        capacity = Math.Max(2, capacity);
        if (capacity == Capacity)
            return;

        var values = new double[capacity + Overscan];
        int keep = Math.Min(_count, values.Length);
        for (int age = keep - 1, i = 0; age >= 0; age--, i++)
            values[i] = GetByAge(age);

        _values = values;
        _count = keep;
        _head = keep % values.Length;
        Capacity = capacity;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _count = 0;
        _head = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
