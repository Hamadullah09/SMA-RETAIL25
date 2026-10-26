using System.Windows;
using System.Windows.Media;

namespace RetailPlus.App.Infrastructure;

/// <summary>A round progress ring with tick marks (the "time tracker" dial of the design).</summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(nameof(RingBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.Gold, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TickBrushProperty = DependencyProperty.Register(nameof(TickBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>0 to 1.</summary>
    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public Brush RingBrush
    {
        get => (Brush)GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    public Brush TickBrush
    {
        get => (Brush)GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double outer = size / 2;
        double ring = Math.Max(8, size * 0.075);
        double arcRadius = outer - ring / 2 - 2;
        double tickOuter = arcRadius - ring / 2 - 6;
        double tickInner = tickOuter - size * 0.05;

        var tickPen = new Pen(TickBrush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var faintBrush = TickBrush.CloneCurrentValue();
        faintBrush.Opacity = 0.35;
        var faintPen = new Pen(faintBrush, 1.5);
        double fraction = Math.Clamp(Fraction, 0, 1);
        for (int i = 0; i < 60; i++)
        {
            double a = (i / 60.0 * 360 - 90) * Math.PI / 180;
            bool major = i % 5 == 0;
            double inner = major ? tickInner - 3 : tickInner;
            var p1 = new Point(center.X + Math.Cos(a) * inner, center.Y + Math.Sin(a) * inner);
            var p2 = new Point(center.X + Math.Cos(a) * tickOuter, center.Y + Math.Sin(a) * tickOuter);
            dc.DrawLine(i / 60.0 < fraction ? tickPen : faintPen, p1, p2);
        }

        var track = new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), ring);
        dc.DrawEllipse(null, track, center, arcRadius, arcRadius);
        if (fraction <= 0) return;
        var pen = new Pen(RingBrush, ring) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (fraction >= 0.999)
        {
            dc.DrawEllipse(null, pen, center, arcRadius, arcRadius);
            return;
        }
        double end = (fraction * 360 - 90) * Math.PI / 180;
        var start = new Point(center.X, center.Y - arcRadius);
        var stop = new Point(center.X + Math.Cos(end) * arcRadius, center.Y + Math.Sin(end) * arcRadius);
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(stop, new Size(arcRadius, arcRadius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        dc.DrawGeometry(null, pen, new PathGeometry([figure]));
    }
}
