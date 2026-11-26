using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SystemWatch.Infrastructure;

namespace SystemWatch.Charts;

public enum GraphUnit
{
    Percent,
    BytesPerSecond,
}

/// <summary>
/// Leichtgewichtiger Live-Graph:
/// <list type="bullet">
/// <item>gleitet zwischen zwei Messwerten stufenlos nach links (statt im Sekundentakt zu springen),</item>
/// <item>zeichnet eine monotone kubische Kurve – weich, aber ohne erfundene Spitzen über/unter den Messwerten,</item>
/// <item>skaliert Datenraten automatisch auf „runde“ Maxima und blendet die Skala sanft um,</item>
/// <item>zeigt beim Überfahren Fadenkreuz, Messpunkt und Wert.</item>
/// </list>
/// <para>
/// Effizienz: Die Kurve wird nur einmal pro Messwert aufgebaut. Für das Gleiten dazwischen wird
/// lediglich eine Verschiebung (<see cref="TranslateTransform"/>) angepasst und der Endpunkt neu
/// gesetzt – so bleibt die App selbst bei flüssiger Darstellung sparsam.
/// </para>
/// </summary>
public sealed class LiveGraph : FrameworkElement, IFrameTarget
{
    private const double LabelBand = 18;
    private const double EdgePadding = 7;
    private const double ScaleTimeConstant = 0.35;
    private const double MinFramesPerSecond = 6;
    private const double FramesPerPixel = 4;

    private static readonly Typeface LabelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface ValueTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x89, 0x87, 0x81));
    private static readonly Brush PrimaryTextBrush = Frozen(Color.FromRgb(0xF0, 0xEF, 0xEC));
    private static readonly Brush SurfaceBrush = Frozen(Color.FromRgb(0x1A, 0x1A, 0x19));
    private static readonly Brush TooltipBrush = Frozen(Color.FromRgb(0x26, 0x26, 0x24));
    private static readonly Pen TooltipBorderPen = FrozenPen(Frozen(Color.FromRgb(0x3A, 0x3A, 0x37)), 1);
    private static readonly Color GridColor = Color.FromRgb(0x2C, 0x2C, 0x2A);
    private static readonly Color BaselineColor = Color.FromRgb(0x38, 0x38, 0x35);
    private static readonly Color CrosshairColor = Color.FromArgb(0x80, 0x89, 0x87, 0x81);

    // ───────────── Abhängigkeitseigenschaften ─────────────

    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(MetricSeries), typeof(LiveGraph), new PropertyMetadata(null, OnSeriesChanged));

    public static readonly DependencyProperty SecondarySeriesProperty = DependencyProperty.Register(
        nameof(SecondarySeries), typeof(MetricSeries), typeof(LiveGraph), new PropertyMetadata(null, OnSeriesChanged));

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(LiveGraph), new PropertyMetadata(Color.FromRgb(0x39, 0x87, 0xE5), OnAppearanceChanged));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(GraphUnit), typeof(LiveGraph), new PropertyMetadata(GraphUnit.Percent, OnAppearanceChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(LiveGraph), new PropertyMetadata(double.NaN, OnAppearanceChanged));

    public static readonly DependencyProperty MinimumScaleProperty = DependencyProperty.Register(
        nameof(MinimumScale), typeof(double), typeof(LiveGraph), new PropertyMetadata(0d, OnAppearanceChanged));

    public static readonly DependencyProperty PrimaryLabelProperty = DependencyProperty.Register(
        nameof(PrimaryLabel), typeof(string), typeof(LiveGraph), new PropertyMetadata(null));

    public static readonly DependencyProperty SecondaryLabelProperty = DependencyProperty.Register(
        nameof(SecondaryLabel), typeof(string), typeof(LiveGraph), new PropertyMetadata(null));

    public MetricSeries? Series
    {
        get => (MetricSeries?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>Optionale zweite Linie (z. B. Upload), ohne Füllung in hellerem Ton.</summary>
    public MetricSeries? SecondarySeries
    {
        get => (MetricSeries?)GetValue(SecondarySeriesProperty);
        set => SetValue(SecondarySeriesProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public GraphUnit Unit
    {
        get => (GraphUnit)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <summary>Feste Obergrenze (z. B. 100 für Prozent). <c>NaN</c> = automatisch.</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Kleinste automatische Obergrenze, damit Grundrauschen nicht als Vollausschlag erscheint.</summary>
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

    // ───────────── Zeichenebenen ─────────────

    private readonly DrawingGroup _root = new();
    private readonly DrawingGroup _chrome = new();      // Raster + Achsenbeschriftung
    private readonly DrawingGroup _curvesClip = new();  // schneidet am Plotrand ab (bewegt sich nicht)
    private readonly DrawingGroup _curves = new();      // Kurven, werden per _scroll verschoben
    private readonly DrawingGroup _dots = new();        // Endpunkte am rechten Rand
    private readonly DrawingGroup _overlay = new();     // Fadenkreuz + Tooltip
    private readonly TranslateTransform _scroll = new();

    private readonly SeriesShape _primaryShape = new();
    private readonly SeriesShape _secondaryShape = new();

    private Pen _linePen = null!;
    private Pen _secondaryPen = null!;
    private Brush _lineBrush = null!;
    private Brush _secondaryBrush = null!;
    private LinearGradientBrush? _fillBrush;
    private Pen? _gridPen;
    private Pen? _baselinePen;
    private Pen? _crosshairPen;

    // ───────────── Zustand ─────────────

    private Rect _plot;
    private double _dpiScale;
    private double _pixelsPerDip = 1;
    private double _displayMax = double.NaN;
    private double _targetMax = double.NaN;
    private double _builtMax = double.NaN;
    private bool _curvesDirty = true;
    private bool _chromeDirty = true;
    private bool _overlayVisible;
    private bool _registered;
    private long _lastUpdate;
    private long _lastSlot;
    private Point? _hover;

    public LiveGraph()
    {
        Focusable = false;
        UpdateBrushes();

        _curves.Transform = _scroll;
        _curvesClip.Children.Add(_curves);
        _root.Children.Add(_chrome);
        _root.Children.Add(_curvesClip);
        _root.Children.Add(_dots);
        _root.Children.Add(_overlay);

        Loaded += (_, _) =>
        {
            FrameClock.EnabledChanged += OnClockEnabledChanged;
            UpdateRegistration();
            Invalidate();
        };
        Unloaded += (_, _) =>
        {
            FrameClock.EnabledChanged -= OnClockEnabledChanged;
            UpdateRegistration();
        };
        IsVisibleChanged += (_, _) =>
        {
            UpdateRegistration();
            Invalidate();
        };
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Unsichtbare Fläche, damit die Maus überall erkannt wird.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        drawingContext.DrawDrawing(_root);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
        Invalidate();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _hover = e.GetPosition(this);
        Update(Stopwatch.GetTimestamp());
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        Update(Stopwatch.GetTimestamp());
    }

    /// <summary>Vom <see cref="FrameClock"/> pro Takt aufgerufen; aktualisiert nur, wenn es sich lohnt.</summary>
    void IFrameTarget.RenderFrame(long now)
    {
        // Feste Zeitschlitze: Gleich schnelle Graphen aktualisieren im selben Takt,
        // WPF braucht dann nur einen Render-Durchlauf für alle.
        long slot = now / Math.Max(1, (long)FrameIntervalTicks());
        if (slot != _lastSlot)
        {
            _lastSlot = slot;
            Update(now);
        }
    }

    /// <summary>
    /// Die Kurve wandert nur wenige Pixel pro Sekunde. Aktualisiert wird so oft, dass jeder Schritt
    /// etwa ¼ Pixel beträgt – für das Auge flüssig, ohne unnötige 60 Bilder pro Sekunde.
    /// Während die Skala umblendet, läuft die volle Bildrate.
    /// </summary>
    private double FrameIntervalTicks()
    {
        var series = Series;
        if (series == null || series.Count < 2 || _plot.Width <= 0)
            return Stopwatch.Frequency;

        if (IsRescaling)
            return Stopwatch.Frequency / 60.0;

        double pixelsPerSecond = _plot.Width / (series.Capacity - 1) / series.IntervalSeconds;
        double framesPerSecond = Math.Max(MinFramesPerSecond, Math.Min(60, pixelsPerSecond * FramesPerPixel));
        return Stopwatch.Frequency / framesPerSecond;
    }

    private bool IsRescaling =>
        !double.IsNaN(_displayMax) && Math.Abs(_displayMax - _targetMax) > _targetMax * 0.003;

    // ───────────── Ereignisse ─────────────

    private static void OnSeriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var graph = (LiveGraph)d;
        if (e.OldValue is MetricSeries oldSeries)
            oldSeries.Changed -= graph.OnSeriesDataChanged;
        if (e.NewValue is MetricSeries newSeries)
            newSeries.Changed += graph.OnSeriesDataChanged;
        graph._displayMax = double.NaN;
        graph.Invalidate();
    }

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var graph = (LiveGraph)d;
        graph.UpdateBrushes();
        graph._displayMax = double.NaN;
        graph.Invalidate();
    }

    /// <summary>Neuer Messwert: Kurve neu aufbauen – sofort, damit Lage und Verschiebung zusammenpassen.</summary>
    private void OnSeriesDataChanged(object? sender, EventArgs e)
    {
        _curvesDirty = true;
        if (IsVisible)
            Update(Stopwatch.GetTimestamp());
    }

    private void OnClockEnabledChanged(object? sender, EventArgs e)
    {
        UpdateRegistration();
        Invalidate();
    }

    private void Invalidate()
    {
        _chromeDirty = true;
        _curvesDirty = true;
        _fillBrush = null;
        _dpiScale = 0;
        if (IsVisible)
            Update(Stopwatch.GetTimestamp());
    }

    private void UpdateRegistration()
    {
        bool wanted = IsLoaded && IsVisible && FrameClock.Enabled;
        if (wanted == _registered)
            return;

        _registered = wanted;
        _lastUpdate = 0;
        if (wanted)
            FrameClock.Register(this);
        else
            FrameClock.Unregister(this);
    }

    private void UpdateBrushes()
    {
        var color = Color;
        _lineBrush = Frozen(color);
        _secondaryBrush = Frozen(Blend(color, Colors.White, 0.45));
        _linePen = FrozenPen(_lineBrush, 2);
        _secondaryPen = FrozenPen(_secondaryBrush, 1.5);
        _fillBrush = null;
    }

    // ───────────── Aktualisieren ─────────────

    private void Update(long now)
    {
        if (!EnsureLayout())
            return;

        var primary = Series;
        var secondary = SecondarySeries;

        // Skala (bei automatischer Skalierung sanft nachführen)
        double targetMax = ComputeTargetMaximum(primary, secondary);
        double seconds = _lastUpdate == 0 ? 0 : (now - _lastUpdate) / (double)Stopwatch.Frequency;
        _lastUpdate = now;

        if (targetMax != _targetMax)
        {
            _targetMax = targetMax;
            _chromeDirty = true;
        }

        if (double.IsNaN(_displayMax) || !_registered || seconds <= 0)
            _displayMax = targetMax;
        else
            _displayMax += (targetMax - _displayMax) * (1 - Math.Exp(-seconds / ScaleTimeConstant));

        if (Math.Abs(_displayMax - _builtMax) > _builtMax * 0.001 || double.IsNaN(_builtMax))
            _curvesDirty = true;

        if (_chromeDirty)
            RebuildChrome();
        if (_curvesDirty)
            RebuildCurves(primary, secondary);

        // Gleiten: Anteil des aktuellen Messintervalls, der schon vergangen ist
        double offset = 0;
        if (_registered && primary != null && primary.Count >= 2)
        {
            double elapsed = (now - primary.LastTimestamp) / (double)Stopwatch.Frequency;
            double progress = Math.Max(0, Math.Min(1, elapsed / primary.IntervalSeconds));
            offset = (1 - progress) * Step(primary);
        }

        _scroll.X = offset;
        UpdateDots(offset);
        UpdateOverlay(offset, primary, secondary);
    }

    /// <summary>Berechnet den Plotbereich. Gibt false zurück, wenn (noch) nichts zu zeichnen ist.</summary>
    private bool EnsureLayout()
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width < 8 || height < LabelBand + 8 || !IsVisible)
            return false;

        var dpi = VisualTreeHelper.GetDpi(this);
        double devicePixel = 1 / dpi.DpiScaleY;
        var plot = new Rect(0, LabelBand, Math.Max(1, width - EdgePadding), height - LabelBand - devicePixel);

        if (plot != _plot || Math.Abs(dpi.DpiScaleY - _dpiScale) > 0.001)
        {
            _plot = plot;
            _dpiScale = dpi.DpiScaleY;
            _pixelsPerDip = dpi.PixelsPerDip;
            _fillBrush = null;
            _chromeDirty = true;
            _curvesDirty = true;

            double hairline = 1 / _dpiScale;
            _gridPen = FrozenPen(Frozen(GridColor), hairline);
            _baselinePen = FrozenPen(Frozen(BaselineColor), hairline);
            _crosshairPen = FrozenPen(Frozen(CrosshairColor), hairline);

            var clip = new RectangleGeometry(new Rect(0, plot.Top - 3, plot.Right, plot.Height + 3 + devicePixel));
            clip.Freeze();
            _curvesClip.ClipGeometry = clip;
        }

        return true;
    }

    /// <summary>Raster bei 0/25/50/75 % der Höhe, Grundlinie und Beschriftung des Maximums.</summary>
    private void RebuildChrome()
    {
        _chromeDirty = false;
        double width = ActualWidth;

        using var dc = _chrome.Open();
        for (int i = 0; i < 4; i++)
        {
            double y = Snap(_plot.Top + _plot.Height * i / 4.0);
            dc.DrawLine(_gridPen, new Point(0, y), new Point(width, y));
        }

        double baseline = Snap(_plot.Bottom);
        dc.DrawLine(_baselinePen, new Point(0, baseline), new Point(width, baseline));

        var label = CreateText(FormatValue(_targetMax), MutedBrush, 11, LabelTypeface);
        dc.DrawText(label, new Point(0, (LabelBand - label.Height) / 2 - 1));
    }

    /// <summary>Baut die Kurven so auf, als wäre der neueste Wert genau am rechten Rand.</summary>
    private void RebuildCurves(MetricSeries? primary, MetricSeries? secondary)
    {
        _curvesDirty = false;
        double max = _displayMax > 0 ? _displayMax : 1;
        _builtMax = _displayMax;

        _primaryShape.Build(primary, _plot, max);
        _secondaryShape.Build(secondary, _plot, max);

        using var dc = _curves.Open();
        if (_secondaryShape.Count >= 2)
            dc.DrawGeometry(null, _secondaryPen, _secondaryShape.CreateGeometry(_plot.Bottom, closed: false));

        if (_primaryShape.Count >= 2)
        {
            dc.DrawGeometry(GetFillBrush(), null, _primaryShape.CreateGeometry(_plot.Bottom, closed: true));
            dc.DrawGeometry(null, _linePen, _primaryShape.CreateGeometry(_plot.Bottom, closed: false));
        }
    }

    /// <summary>Endpunkte („Live-Wert“) genau auf der Kurve am rechten Rand.</summary>
    private void UpdateDots(double offset)
    {
        double edge = _plot.Right - offset; // in Kurvenkoordinaten
        using var dc = _dots.Open();
        if (_secondaryShape.Count >= 2)
            DrawDot(dc, new Point(_plot.Right, _secondaryShape.EvaluateAt(edge)), _secondaryBrush, 3);
        if (_primaryShape.Count >= 2)
            DrawDot(dc, new Point(_plot.Right, _primaryShape.EvaluateAt(edge)), _lineBrush, 4);
    }

    private void UpdateOverlay(double offset, MetricSeries? primary, MetricSeries? secondary)
    {
        bool show = _hover is { } hover && primary != null && primary.Count >= 2 &&
                    hover.X >= 0 && hover.X <= ActualWidth && hover.Y >= 0 && hover.Y <= ActualHeight;
        if (!show && !_overlayVisible)
            return;

        using var dc = _overlay.Open();
        _overlayVisible = show && DrawHover(dc, _hover!.Value.X, offset, primary!, secondary);
    }

    private bool DrawHover(DrawingContext dc, double mouseX, double offset, MetricSeries primary, MetricSeries? secondary)
    {
        double step = Step(primary);
        double newestX = _plot.Right + offset;
        double max = _displayMax > 0 ? _displayMax : 1;

        int age = (int)Math.Round((newestX - Math.Min(mouseX, _plot.Right)) / step);
        if (newestX - age * step > _plot.Right + 0.5)
            age++;
        if (age < 0 || age >= primary.Count)
            return false;

        double x = newestX - age * step;
        if (x < _plot.Left)
            return false;

        dc.DrawLine(_crosshairPen, new Point(x, _plot.Top), new Point(x, _plot.Bottom));

        double primaryValue = primary.GetByAge(age);
        double? secondaryValue = secondary != null && age < secondary.Count ? secondary.GetByAge(age) : null;

        if (secondaryValue is { } sv)
            DrawDot(dc, new Point(x, ValueToY(sv, max)), _secondaryBrush, 3);
        DrawDot(dc, new Point(x, ValueToY(primaryValue, max)), _lineBrush, 4);

        // Tooltip: Wert(e) und Zeitpunkt
        int lines = secondaryValue.HasValue ? 3 : 2;
        var texts = new FormattedText[lines];
        texts[0] = CreateText(Describe(PrimaryLabel, primaryValue), PrimaryTextBrush, 12, ValueTypeface);
        if (secondaryValue is { } second)
            texts[1] = CreateText(Describe(SecondaryLabel, second), PrimaryTextBrush, 12, ValueTypeface);
        texts[lines - 1] = CreateText(Format.Ago(age * primary.IntervalSeconds), MutedBrush, 11, LabelTypeface);

        bool keyed = secondaryValue.HasValue;
        double keyWidth = keyed ? 14 : 0;
        double textWidth = 0, textHeight = 0;
        foreach (var text in texts)
        {
            textWidth = Math.Max(textWidth, text.WidthIncludingTrailingWhitespace);
            textHeight += text.Height;
        }

        const double padX = 10, padY = 7;
        double boxWidth = textWidth + keyWidth + padX * 2;
        double boxHeight = textHeight + padY * 2 + 2;
        double boxX = x + 12;
        if (boxX + boxWidth > ActualWidth)
            boxX = x - 12 - boxWidth;
        boxX = Math.Max(0, boxX);

        var box = new Rect(Math.Round(boxX), Math.Round(_plot.Top + 4), Math.Ceiling(boxWidth), Math.Ceiling(boxHeight));
        dc.DrawRoundedRectangle(TooltipBrush, TooltipBorderPen, box, 6, 6);

        double y = box.Y + padY;
        for (int i = 0; i < texts.Length; i++)
        {
            bool isValueLine = i < lines - 1;
            if (keyed && isValueLine)
            {
                var keyBrush = i == 0 ? _lineBrush : _secondaryBrush;
                double keyY = y + texts[i].Height / 2;
                dc.DrawRoundedRectangle(keyBrush, null, new Rect(box.X + padX, keyY - 1.5, 9, 3), 1.5, 1.5);
            }

            dc.DrawText(texts[i], new Point(box.X + padX + (isValueLine ? keyWidth : 0), y));
            y += texts[i].Height + (i == lines - 2 ? 2 : 0);
        }

        return true;
    }

    private string Describe(string? label, double value) =>
        string.IsNullOrEmpty(label) ? FormatValue(value) : label + "  " + FormatValue(value);

    private static void DrawDot(DrawingContext dc, Point center, Brush brush, double radius)
    {
        dc.DrawEllipse(SurfaceBrush, null, center, radius + 2, radius + 2);
        dc.DrawEllipse(brush, null, center, radius, radius);
    }

    private double ValueToY(double value, double max)
    {
        double ratio = value / max;
        ratio = ratio < 0 ? 0 : ratio > 1 ? 1 : ratio;
        return _plot.Bottom - ratio * _plot.Height;
    }

    private double Step(MetricSeries series) => _plot.Width / (series.Capacity - 1);

    // ───────────── Skala & Text ─────────────

    private double ComputeTargetMaximum(MetricSeries? primary, MetricSeries? secondary)
    {
        if (!double.IsNaN(Maximum) && Maximum > 0)
            return Maximum;

        double peak = Math.Max(Peak(primary), Peak(secondary));
        return NiceCeiling(Math.Max(peak * 1.1, Math.Max(MinimumScale, 1)));
    }

    private static double Peak(MetricSeries? series)
    {
        if (series == null)
            return 0;

        double peak = 0;
        int visible = Math.Min(series.Count, series.Capacity + 1);
        for (int age = 0; age < visible; age++)
            peak = Math.Max(peak, series.GetByAge(age));
        return peak;
    }

    /// <summary>Rundet auf 1, 2 oder 5 × 10ⁿ in der passenden Einheit (KB, MB, GB …).</summary>
    private double NiceCeiling(double value)
    {
        if (Unit != GraphUnit.BytesPerSecond)
            return NiceDecimal(value);

        double unit = 1;
        while (value >= unit * 1000 && unit < 1024d * 1024 * 1024 * 1024)
            unit *= 1024;

        double nice = NiceDecimal(value / unit);
        return nice >= 1000 ? unit * 1024 : nice * unit;
    }

    private static double NiceDecimal(double value)
    {
        if (value <= 0)
            return 1;

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        double normalized = value / magnitude;
        double nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    private string FormatValue(double value) => Unit switch
    {
        GraphUnit.BytesPerSecond => Format.Speed(value),
        _ => Format.Percent(value),
    };

    private FormattedText CreateText(string text, Brush brush, double size, Typeface typeface) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush, _pixelsPerDip);

    // ───────────── Hilfen ─────────────

    private Brush GetFillBrush()
    {
        if (_fillBrush != null)
            return _fillBrush;

        // Verlauf relativ zur Fläche: Direkt unter der Linie ist die Füllung immer gleich kräftig,
        // auch wenn die Kurve nur knapp über der Grundlinie verläuft.
        var color = Color;
        _fillBrush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(0x38, color.R, color.G, color.B), 0),
                new(Color.FromArgb(0x06, color.R, color.G, color.B), 1),
            },
            new Point(0, 0),
            new Point(0, 1));
        _fillBrush.Freeze();
        return _fillBrush;
    }

    /// <summary>Rastet eine 1-Pixel-Linie auf ein Gerätepixel ein, damit sie scharf bleibt.</summary>
    private double Snap(double value) => (Math.Floor(value * _dpiScale) + 0.5) / _dpiScale;

    private static Color Blend(Color a, Color b, double amount) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * amount),
        (byte)(a.G + (b.G - a.G) * amount),
        (byte)(a.B + (b.B - a.B) * amount));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        return pen;
    }

    /// <summary>Die Stützpunkte einer Reihe als monotone kubische Kurve.</summary>
    private sealed class SeriesShape
    {
        private Point[] _points = new Point[64];
        private double[] _slopes = new double[64];
        private double[] _tangents = new double[64];

        public int Count { get; private set; }

        /// <summary>
        /// Rechnet die sichtbaren Werte (alt → neu) in Punkte um. Basis: neuester Wert am rechten Rand;
        /// links wird so viel vorgehalten, dass auch bei voller Verschiebung keine Lücke entsteht.
        /// </summary>
        public void Build(MetricSeries? series, Rect plot, double max)
        {
            Count = 0;
            if (series == null || series.Count < 2)
                return;

            double step = plot.Width / (series.Capacity - 1);
            double newestX = plot.Right;
            int oldestAge = Math.Min(series.Count - 1, (int)Math.Ceiling((newestX + step - plot.Left) / step) + 1);

            Ensure(oldestAge + 1);
            for (int age = oldestAge; age >= 0; age--)
            {
                double ratio = series.GetByAge(age) / max;
                ratio = ratio < 0 ? 0 : ratio > 1 ? 1 : ratio;
                _points[Count++] = new Point(newestX - age * step, plot.Bottom - ratio * plot.Height);
            }

            ComputeTangents();
        }

        public StreamGeometry CreateGeometry(double baseline, bool closed)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                if (closed)
                {
                    ctx.BeginFigure(new Point(_points[0].X, baseline), true, true);
                    ctx.LineTo(_points[0], false, false);
                }
                else
                {
                    ctx.BeginFigure(_points[0], false, false);
                }

                for (int k = 0; k < Count - 1; k++)
                {
                    Point a = _points[k];
                    Point b = _points[k + 1];
                    double third = (b.X - a.X) / 3;
                    ctx.BezierTo(
                        new Point(a.X + third, a.Y + _tangents[k] * third),
                        new Point(b.X - third, b.Y - _tangents[k + 1] * third),
                        b, !closed, true);
                }

                if (closed)
                    ctx.LineTo(new Point(_points[Count - 1].X, baseline), false, false);
            }

            geometry.Freeze();
            return geometry;
        }

        /// <summary>Y-Wert der Kurve an Position <paramref name="x"/> (Hermite-Auswertung).</summary>
        public double EvaluateAt(double x)
        {
            if (x >= _points[Count - 1].X)
                return _points[Count - 1].Y;

            for (int k = Count - 2; k >= 0; k--)
            {
                Point a = _points[k];
                if (x < a.X)
                    continue;

                Point b = _points[k + 1];
                double h = b.X - a.X;
                double t = (x - a.X) / h;
                double t2 = t * t;
                double t3 = t2 * t;
                return (2 * t3 - 3 * t2 + 1) * a.Y + (t3 - 2 * t2 + t) * h * _tangents[k] +
                       (-2 * t3 + 3 * t2) * b.Y + (t3 - t2) * h * _tangents[k + 1];
            }

            return _points[0].Y;
        }

        /// <summary>
        /// Tangenten nach Fritsch-Butland (harmonisches Mittel): Die Kurve bleibt zwischen zwei
        /// Messwerten monoton und schießt nie über Spitzen oder unter null hinaus.
        /// </summary>
        private void ComputeTangents()
        {
            int n = Count;
            for (int k = 0; k < n - 1; k++)
                _slopes[k] = (_points[k + 1].Y - _points[k].Y) / (_points[k + 1].X - _points[k].X);

            _tangents[0] = _slopes[0];
            _tangents[n - 1] = _slopes[n - 2];
            for (int k = 1; k < n - 1; k++)
            {
                double s0 = _slopes[k - 1];
                double s1 = _slopes[k];
                _tangents[k] = s0 * s1 <= 0 ? 0 : 2 * s0 * s1 / (s0 + s1);
            }
        }

        private void Ensure(int size)
        {
            if (_points.Length >= size)
                return;

            int capacity = Math.Max(size, _points.Length * 2);
            _points = new Point[capacity];
            _slopes = new double[capacity];
            _tangents = new double[capacity];
        }
    }
}
