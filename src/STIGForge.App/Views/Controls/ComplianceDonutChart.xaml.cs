using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace STIGForge.App.Views.Controls;

public partial class ComplianceDonutChart : UserControl
{
    private const double MinThickness = 12.0;
    private const double MaxThickness = 28.0;
    private const double InnerRadiusFactor = 0.35;
    private const double MaxSweepAngle = 359.999d;

    public static readonly DependencyProperty PassValueProperty =
        DependencyProperty.Register(
            nameof(PassValue),
            typeof(double),
            typeof(ComplianceDonutChart),
            new PropertyMetadata(0d, OnSegmentValueChanged));

    public static readonly DependencyProperty FailValueProperty =
        DependencyProperty.Register(
            nameof(FailValue),
            typeof(double),
            typeof(ComplianceDonutChart),
            new PropertyMetadata(0d, OnSegmentValueChanged));

    public static readonly DependencyProperty OtherValueProperty =
        DependencyProperty.Register(
            nameof(OtherValue),
            typeof(double),
            typeof(ComplianceDonutChart),
            new PropertyMetadata(0d, OnSegmentValueChanged));

    public static readonly DependencyProperty CenterTextProperty =
        DependencyProperty.Register(
            nameof(CenterText),
            typeof(string),
            typeof(ComplianceDonutChart),
            new PropertyMetadata(string.Empty));

    public ComplianceDonutChart()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ChartCanvas.SizeChanged += OnChartCanvasSizeChanged;
    }

    public double PassValue
    {
        get => (double)GetValue(PassValueProperty);
        set => SetValue(PassValueProperty, value);
    }

    public double FailValue
    {
        get => (double)GetValue(FailValueProperty);
        set => SetValue(FailValueProperty, value);
    }

    public double OtherValue
    {
        get => (double)GetValue(OtherValueProperty);
        set => SetValue(OtherValueProperty, value);
    }

    public string CenterText
    {
        get => (string)GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    private static void OnSegmentValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComplianceDonutChart chart)
            chart.UpdateChart();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        UpdateChart();
    }

    private void OnChartCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!double.Equals(e.PreviousSize.Width, e.NewSize.Width) || !double.Equals(e.PreviousSize.Height, e.NewSize.Height))
            UpdateChart();
    }

    private void UpdateChart()
    {
        if (!IsLoaded)
            return;

        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var diameter = Math.Min(width, height);
        if (diameter <= 0)
            return;

        var center = new Point(width / 2d, height / 2d);
        var outerRadius = diameter / 2d;
        var thickness = Math.Clamp(outerRadius * 0.27, MinThickness, MaxThickness);
        var innerRadius = Math.Max(outerRadius - thickness, outerRadius * InnerRadiusFactor);

        ChartCanvas.Children.Clear();
        ChartCanvas.Children.Add(CreateBackgroundRing(center, outerRadius, innerRadius));

        var total = Math.Max(0, PassValue) + Math.Max(0, FailValue) + Math.Max(0, OtherValue);
        if (total <= 0)
            return;

        var startAngle = -90d;
        foreach (var segment in new[]
        {
            new SegmentData(PassValue, "SuccessBrush", Brushes.LimeGreen),
            new SegmentData(FailValue, "DangerBrush", Brushes.IndianRed),
            new SegmentData(OtherValue, "WarningBrush", Brushes.Goldenrod)
        })
        {
            if (segment.Value <= 0)
                continue;

            var sweepAngle = Math.Min(MaxSweepAngle, segment.Value / total * 360);
            var fill = ResolveBrush(segment.BrushKey, segment.Fallback);
            ChartCanvas.Children.Add(CreateSegment(startAngle, sweepAngle, center, outerRadius, innerRadius, fill));
            startAngle += sweepAngle;
        }
    }

    private UIElement CreateBackgroundRing(Point center, double outerRadius, double innerRadius)
    {
        var thickness = outerRadius - innerRadius;
        var strokeRadius = (outerRadius + innerRadius) / 2d;
        var brush = ResolveBrush("SurfaceBrush", new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)));
        var ellipse = new Ellipse
        {
            Width = strokeRadius * 2,
            Height = strokeRadius * 2,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent
        };

        Canvas.SetLeft(ellipse, center.X - strokeRadius);
        Canvas.SetTop(ellipse, center.Y - strokeRadius);
        return ellipse;
    }

    private Path CreateSegment(double startAngle, double sweepAngle, Point center, double outerRadius, double innerRadius, Brush fill)
    {
        var figure = new PathFigure
        {
            StartPoint = GetPoint(center, outerRadius, startAngle),
            IsClosed = true
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = GetPoint(center, outerRadius, startAngle + sweepAngle),
            Size = new Size(outerRadius, outerRadius),
            IsLargeArc = sweepAngle >= 180d,
            SweepDirection = SweepDirection.Clockwise,
            RotationAngle = 0,
            IsStroked = true
        });

        figure.Segments.Add(new LineSegment(GetPoint(center, innerRadius, startAngle + sweepAngle), true));

        figure.Segments.Add(new ArcSegment
        {
            Point = GetPoint(center, innerRadius, startAngle),
            Size = new Size(innerRadius, innerRadius),
            IsLargeArc = sweepAngle >= 180d,
            SweepDirection = SweepDirection.Counterclockwise,
            RotationAngle = 0,
            IsStroked = true
        });

        figure.Segments.Add(new LineSegment(figure.StartPoint, true));

        return new Path
        {
            Data = new PathGeometry { Figures = new PathFigureCollection { figure } },
            Fill = fill,
            Stroke = Brushes.Transparent
        };
    }

    private Brush ResolveBrush(string resourceKey, Brush fallback)
    {
        if (TryFindResource(resourceKey) is Brush brush)
            return brush;

        return fallback;
    }

    private static Point GetPoint(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180d;
        return new Point(center.X + Math.Cos(radians) * radius, center.Y + Math.Sin(radians) * radius);
    }

    private readonly record struct SegmentData(double Value, string BrushKey, Brush Fallback);
}
