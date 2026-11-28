using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SystemWatch.Charts;
using SystemWatch.Infrastructure;

namespace SystemWatch.Views;

/// <summary>Kachel mit Titel, Hauptwert, Live-Graph und Kennzahlen. DataContext: <c>MetricViewModel</c>.</summary>
public partial class MetricCard : UserControl
{
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Color), typeof(MetricCard), new PropertyMetadata(Colors.SteelBlue, OnAccentChanged));

    private static readonly DependencyPropertyKey AccentBrushKey = DependencyProperty.RegisterReadOnly(
        nameof(AccentBrush), typeof(Brush), typeof(MetricCard), new PropertyMetadata(Brushes.SteelBlue));

    private static readonly DependencyPropertyKey SecondaryAccentBrushKey = DependencyProperty.RegisterReadOnly(
        nameof(SecondaryAccentBrush), typeof(Brush), typeof(MetricCard), new PropertyMetadata(Brushes.LightSteelBlue));

    public static readonly DependencyProperty AccentBrushProperty = AccentBrushKey.DependencyProperty;
    public static readonly DependencyProperty SecondaryAccentBrushProperty = SecondaryAccentBrushKey.DependencyProperty;

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(GraphUnit), typeof(MetricCard), new PropertyMetadata(GraphUnit.Percent));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(MetricCard), new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty MinimumScaleProperty = DependencyProperty.Register(
        nameof(MinimumScale), typeof(double), typeof(MetricCard), new PropertyMetadata(0d));

    public static readonly DependencyProperty PrimaryLabelProperty = DependencyProperty.Register(
        nameof(PrimaryLabel), typeof(string), typeof(MetricCard), new PropertyMetadata(null));

    public static readonly DependencyProperty SecondaryLabelProperty = DependencyProperty.Register(
        nameof(SecondaryLabel), typeof(string), typeof(MetricCard), new PropertyMetadata(null));

    public static readonly DependencyProperty ExtraContentProperty = DependencyProperty.Register(
        nameof(ExtraContent), typeof(object), typeof(MetricCard), new PropertyMetadata(null));

    public MetricCard()
    {
        InitializeComponent();
        UpdateBrushes();

        // Die zweite Linie wird je nach Design heller oder dunkler – auch wenn die Akzentfarbe gleich bleibt.
        Loaded += (_, _) =>
        {
            ThemeManager.Changed += OnThemeChanged;
            UpdateBrushes();
        };
        Unloaded += (_, _) => ThemeManager.Changed -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, System.EventArgs e) => UpdateBrushes();

    public Color Accent
    {
        get => (Color)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public Brush AccentBrush => (Brush)GetValue(AccentBrushProperty);

    /// <summary>Abgestufte Variante für die zweite Linie – passt zur Darstellung im Graphen.</summary>
    public Brush SecondaryAccentBrush => (Brush)GetValue(SecondaryAccentBrushProperty);

    public GraphUnit Unit
    {
        get => (GraphUnit)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double MinimumScale
    {
        get => (double)GetValue(MinimumScaleProperty);
        set => SetValue(MinimumScaleProperty, value);
    }

    public string? PrimaryLabel
    {
        get => (string?)GetValue(PrimaryLabelProperty);
        set => SetValue(PrimaryLabelProperty, value);
    }

    public string? SecondaryLabel
    {
        get => (string?)GetValue(SecondaryLabelProperty);
        set => SetValue(SecondaryLabelProperty, value);
    }

    public object? ExtraContent
    {
        get => GetValue(ExtraContentProperty);
        set => SetValue(ExtraContentProperty, value);
    }

    private static void OnAccentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MetricCard)d).UpdateBrushes();

    private void UpdateBrushes()
    {
        var color = Accent;
        var light = ThemeManager.SecondaryOf(color);

        var accent = new SolidColorBrush(color);
        accent.Freeze();
        var secondary = new SolidColorBrush(light);
        secondary.Freeze();

        SetValue(AccentBrushKey, accent);
        SetValue(SecondaryAccentBrushKey, secondary);
    }
}
