using System;
using System.Windows;
using System.Windows.Controls;

namespace SystemWatch.Views;

/// <summary>
/// Ordnet Kennzahlen gleichmäßig in einer Reihe an. Reicht die Breite nicht, wird auf
/// ausgewogene Zeilen umgebrochen (z. B. 2 × 2 statt 3 + 1) – nichts wird abgeschnitten.
/// </summary>
public sealed class StatsPanel : Panel
{
    public double ColumnSpacing { get; set; } = 10;
    public double RowSpacing { get; set; } = 10;

    private int _columns = 1;
    private double _cellHeight;

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = 0;
        double widest = 0;
        _cellHeight = 0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed)
                continue;

            count++;
            widest = Math.Max(widest, child.DesiredSize.Width);
            _cellHeight = Math.Max(_cellHeight, child.DesiredSize.Height);
        }

        if (count == 0)
            return new Size(0, 0);

        _columns = count;
        if (!double.IsInfinity(availableSize.Width))
        {
            int fit = Math.Max(1, (int)((availableSize.Width + ColumnSpacing) / (widest + ColumnSpacing)));
            if (fit < count)
            {
                int rows = (int)Math.Ceiling(count / (double)fit);
                _columns = (int)Math.Ceiling(count / (double)rows);
            }
        }

        int rowCount = (int)Math.Ceiling(count / (double)_columns);
        double width = double.IsInfinity(availableSize.Width)
            ? _columns * (widest + ColumnSpacing) - ColumnSpacing
            : availableSize.Width;
        return new Size(width, rowCount * _cellHeight + (rowCount - 1) * RowSpacing);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double cellWidth = Math.Max(0, (finalSize.Width - (_columns - 1) * ColumnSpacing) / _columns);
        int index = 0;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            int row = index / _columns;
            int column = index % _columns;
            child.Arrange(new Rect(column * (cellWidth + ColumnSpacing), row * (_cellHeight + RowSpacing), cellWidth, _cellHeight));
            index++;
        }

        return finalSize;
    }
}
