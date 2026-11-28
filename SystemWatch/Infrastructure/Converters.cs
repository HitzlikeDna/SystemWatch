using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SystemWatch.Infrastructure;

/// <summary>true, wenn der Wert dem Parameter entspricht – für RadioButtons (Segmente, Navigation).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value != null && parameter != null &&
        string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter == null)
            return Binding.DoNothing;

        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return type.IsEnum
            ? Enum.Parse(type, parameter.ToString()!, ignoreCase: true)
            : System.Convert.ChangeType(parameter, type, CultureInfo.InvariantCulture);
    }
}

/// <summary>Sichtbar, wenn der Wert dem Parameter entspricht.</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value != null && parameter != null &&
        string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value switch
        {
            bool b => b,
            string s => s.Length > 0,
            null => false,
            _ => true,
        };
        return visible ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Hinterlegt Tabellenwerte mit einer Farbe, deren Deckkraft mit dem Wert steigt (0…1).
/// Parameter: Ressourcenschlüssel der Farbe (z. B. "CpuColor") oder Hex (#RRGGBB) –
/// der Schlüssel folgt dem aktiven Design. Pinsel werden in 24 Stufen zwischengespeichert.
/// </summary>
public sealed class HeatBrushConverter : IValueConverter
{
    private const int Levels = 24;
    private static readonly Dictionary<Color, Brush[]> Cache = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double heat || heat <= 0.001 || parameter is not string key)
            return Brushes.Transparent;

        var color = key.StartsWith("#", StringComparison.Ordinal)
            ? (Color)ColorConverter.ConvertFromString(key)
            : ThemeManager.GetColor(key, Colors.SteelBlue);

        if (!Cache.TryGetValue(color, out var brushes))
        {
            brushes = new Brush[Levels + 1];
            for (int i = 0; i <= Levels; i++)
            {
                // Mindestens ein Hauch Farbe, höchstens 42 % Deckkraft – Text bleibt lesbar.
                byte alpha = (byte)(255 * (0.05 + 0.37 * i / Levels));
                var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
                brush.Freeze();
                brushes[i] = brush;
            }

            Cache[color] = brushes;
        }

        int level = (int)Math.Round(Math.Min(1, heat) * Levels);
        return brushes[level];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
