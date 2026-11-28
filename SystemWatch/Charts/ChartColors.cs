using System.Windows.Media;
using SystemWatch.Infrastructure;

namespace SystemWatch.Charts;

/// <summary>Eingefrorene Pinsel für selbst gezeichnete Graphen, passend zum aktiven Design.</summary>
internal sealed class ChartColors
{
    private ChartColors(Color muted, Color primaryText, Color surface, Color tooltip, Color tooltipBorder,
        Color grid, Color baseline)
    {
        Muted = Frozen(muted);
        PrimaryText = Frozen(primaryText);
        Surface = Frozen(surface);
        Tooltip = Frozen(tooltip);
        TooltipBorder = new Pen(Frozen(tooltipBorder), 1);
        TooltipBorder.Freeze();
        Grid = Frozen(grid);
        Baseline = Frozen(baseline);
        Crosshair = Frozen(Color.FromArgb(0x80, muted.R, muted.G, muted.B));
    }

    public Brush Muted { get; }
    public Brush PrimaryText { get; }
    public Brush Surface { get; }
    public Brush Tooltip { get; }
    public Pen TooltipBorder { get; }
    public Brush Grid { get; }
    public Brush Baseline { get; }
    public Brush Crosshair { get; }

    /// <summary>Liest die Farben aus den Anwendungsressourcen (Fallback: dunkles Design).</summary>
    public static ChartColors Load() => new(
        ThemeManager.GetColor("TextMutedColor", Color.FromRgb(0x89, 0x87, 0x81)),
        ThemeManager.GetColor("TextPrimaryColor", Color.FromRgb(0xF0, 0xEF, 0xEC)),
        ThemeManager.GetColor("SurfaceColor", Color.FromRgb(0x1A, 0x1A, 0x19)),
        ThemeManager.GetColor("ChartTooltipColor", Color.FromRgb(0x26, 0x26, 0x24)),
        ThemeManager.GetColor("ChartTooltipBorderColor", Color.FromRgb(0x3A, 0x3A, 0x37)),
        ThemeManager.GetColor("ChartGridColor", Color.FromRgb(0x2C, 0x2C, 0x2A)),
        ThemeManager.GetColor("ChartBaselineColor", Color.FromRgb(0x38, 0x38, 0x35)));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
