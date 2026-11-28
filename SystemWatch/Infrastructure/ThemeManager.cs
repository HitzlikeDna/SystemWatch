using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SystemWatch.Infrastructure;

public enum AppTheme
{
    Dark,
    Light,
    System,
}

/// <summary>
/// Lädt das Farbsystem (Themes/Dark.xaml bzw. Themes/Light.xaml) in die Anwendungsressourcen.
/// Alle Oberflächen beziehen ihre Farben per DynamicResource und folgen damit sofort;
/// selbst gezeichnete Elemente (Graphen, Titelleiste) hören auf <see cref="Changed"/>.
/// </summary>
internal static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static AppTheme _mode = AppTheme.Dark;
    private static bool _initialized;
    private static bool _listening;

    /// <summary>Wird nach jedem Wechsel des Farbsystems auf dem UI-Thread ausgelöst.</summary>
    public static event EventHandler? Changed;

    /// <summary>Das tatsächlich angezeigte Design (bei „Wie Windows“ aufgelöst).</summary>
    public static bool IsLight { get; private set; }

    /// <summary>Zählt jeden Wechsel – für Zwischenspeicher, die nachladen müssen.</summary>
    public static int Version { get; private set; }

    public static AppTheme Mode
    {
        get => _mode;
        set
        {
            if (_mode == value && _initialized)
                return;
            _mode = value;
            UpdateSystemListener();
            Apply();
        }
    }

    public static void Initialize(AppTheme mode)
    {
        _mode = mode;
        UpdateSystemListener();
        Apply();
    }

    /// <summary>Farbe aus dem aktiven Farbsystem, z. B. "PageColor".</summary>
    public static Color GetColor(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) is Color color ? color : fallback;

    /// <summary>
    /// Zweite Linie eines Graphen (Upload, Schreiben): im dunklen Design heller,
    /// im hellen dunkler als die Hauptfarbe – so bleibt sie auf beiden Flächen gut sichtbar.
    /// </summary>
    public static Color SecondaryOf(Color color) => IsLight
        ? Blend(color, Colors.Black, 0.35)
        : Blend(color, Colors.White, 0.45);

    private static void Apply()
    {
        bool light = _mode switch
        {
            AppTheme.Light => true,
            AppTheme.System => WindowsUsesLightTheme(),
            _ => false,
        };

        if (_initialized && light == IsLight)
            return;

        var app = Application.Current;
        if (app == null)
            return;

        bool first = !_initialized;
        IsLight = light;
        _initialized = true;

        // App.xaml lädt bereits das dunkle Design – beim Start nur bei „Hell“ austauschen.
        if (first && !light)
            return;

        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/SystemWatch;component/Themes/{(light ? "Light" : "Dark")}.xaml"),
        };

        // Das Farbsystem ist immer das erste Wörterbuch (siehe App.xaml).
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > 0)
            merged[0] = dictionary;
        else
            merged.Add(dictionary);

        Version++;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void UpdateSystemListener()
    {
        bool wanted = _mode == AppTheme.System;
        if (wanted == _listening)
            return;

        _listening = wanted;
        if (wanted)
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        else
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
            return;

        // SystemEvents meldet sich ggf. auf einem eigenen Thread.
        Application.Current?.Dispatcher.BeginInvoke(new Action(Apply));
    }

    private static Color Blend(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * amount),
        (byte)Math.Round(a.G + (b.G - a.G) * amount),
        (byte)Math.Round(a.B + (b.B - a.B) * amount));

    /// <summary>Stoppt das Mithören der Windows-Einstellung (beim Beenden).</summary>
    public static void Shutdown()
    {
        if (_listening)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _listening = false;
        }
    }
}
