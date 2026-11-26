using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace SystemWatch.Charts;

/// <summary>Schmaler Füllbalken (0…1), der bei Änderungen sanft zum neuen Wert gleitet.</summary>
public sealed class UsageBar : FrameworkElement, IFrameTarget
{
    private const double EaseTimeConstant = 0.12;

    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(UsageBar), new PropertyMetadata(0d, OnFractionChanged));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(UsageBar),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(UsageBar),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    private double _shown;
    private double _target;
    private long _lastFrame;
    private bool _animating;

    public UsageBar()
    {
        Height = 4;
        SnapsToDevicePixels = true;
        Unloaded += (_, _) => StopAnimation(jumpToTarget: true);
    }

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    private static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (UsageBar)d;
        bar._target = Clamp((double)e.NewValue);

        if (!FrameClock.Enabled || !bar.IsLoaded || !bar.IsVisible)
        {
            bar.StopAnimation(jumpToTarget: true);
            return;
        }

        if (!bar._animating)
        {
            bar._animating = true;
            bar._lastFrame = Stopwatch.GetTimestamp();
            FrameClock.Register(bar);
        }
    }

    void IFrameTarget.RenderFrame(long now)
    {
        double seconds = (now - _lastFrame) / (double)Stopwatch.Frequency;
        _lastFrame = now;

        _shown += (_target - _shown) * (1 - Math.Exp(-seconds / EaseTimeConstant));
        if (Math.Abs(_target - _shown) < 0.002 || !FrameClock.Enabled)
            StopAnimation(jumpToTarget: true);
        else
            InvalidateVisual();
    }

    private void StopAnimation(bool jumpToTarget)
    {
        if (_animating)
        {
            _animating = false;
            FrameClock.Unregister(this);
        }

        if (jumpToTarget)
        {
            _shown = _target;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        double radius = height / 2;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, width, height), radius, radius);

        if (_shown <= 0.0005)
            return;

        double fillWidth = Math.Max(height, width * _shown);
        dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, fillWidth, height), radius, radius);
    }

    private static double Clamp(double value) => double.IsNaN(value) ? 0 : Math.Max(0, Math.Min(1, value));
}
