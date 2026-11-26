using System;
using System.Windows;
using System.Windows.Controls;

namespace SystemWatch.Views;

public partial class OverviewView : UserControl
{
    private const int CardCount = 6;
    private const double MinCardHeight = 300;

    public OverviewView()
    {
        InitializeComponent();
        Scroller.SizeChanged += (_, _) => UpdateLayoutGrid();
    }

    /// <summary>3, 2 oder 1 Spalte je nach Breite; Kacheln füllen die Höhe, solange Platz ist.</summary>
    private void UpdateLayoutGrid()
    {
        double width = Scroller.ActualWidth;
        double height = Scroller.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        int columns = width >= 940 ? 3 : width >= 620 ? 2 : 1;
        int rows = (int)Math.Ceiling(CardCount / (double)columns);

        CardGrid.Columns = columns;
        CardGrid.Margin = new Thickness(0);
        CardGrid.Height = rows * Math.Max(MinCardHeight, Math.Floor(height / rows));
    }
}
