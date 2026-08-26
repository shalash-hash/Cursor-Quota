using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace Quota.Controls;

public partial class GraduatedProgressBar : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(GraduatedProgressBar),
            new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(
            nameof(Maximum),
            typeof(double),
            typeof(GraduatedProgressBar),
            new PropertyMetadata(100d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(
            nameof(FillBrush),
            typeof(Brush),
            typeof(GraduatedProgressBar),
            new PropertyMetadata(null, OnFillBrushChanged));

    public static readonly DependencyProperty SecondaryValueProperty =
        DependencyProperty.Register(
            nameof(SecondaryValue),
            typeof(double),
            typeof(GraduatedProgressBar),
            new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty SecondaryBrushProperty =
        DependencyProperty.Register(
            nameof(SecondaryBrush),
            typeof(Brush),
            typeof(GraduatedProgressBar),
            new PropertyMetadata(null, OnSecondaryBrushChanged));

    public GraduatedProgressBar()
    {
        InitializeComponent();
        Track.SizeChanged += (_, _) => UpdateFills();
        Loaded += (_, _) => UpdateFills();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public double SecondaryValue
    {
        get => (double)GetValue(SecondaryValueProperty);
        set => SetValue(SecondaryValueProperty, value);
    }

    public Brush? SecondaryBrush
    {
        get => (Brush?)GetValue(SecondaryBrushProperty);
        set => SetValue(SecondaryBrushProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GraduatedProgressBar bar)
            bar.UpdateFills();
    }

    private static void OnFillBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GraduatedProgressBar bar)
            bar.ApplyFillBrush(bar.PrimaryFill, bar.FillBrush);
    }

    private static void OnSecondaryBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GraduatedProgressBar bar)
            bar.ApplyFillBrush(bar.SecondaryFill, bar.SecondaryBrush);
    }

    private void UpdateFills()
    {
        var innerWidth = Math.Max(0, Track.ActualWidth - 2);
        if (innerWidth <= 0)
            return;

        var maximum = Maximum > 0 ? Maximum : 100;
        var primaryPercent = Math.Clamp(Value, 0, maximum) / maximum;
        var secondaryPercent = Math.Clamp(SecondaryValue, 0, maximum) / maximum;

        var primaryWidth = innerWidth * primaryPercent;
        var secondaryWidth = innerWidth * secondaryPercent;

        PrimaryFill.Width = primaryWidth;
        SecondaryFill.Width = secondaryWidth;
        SecondaryFill.Margin = new Thickness(primaryWidth, 0, 0, 0);
        SecondaryFill.Visibility = secondaryWidth > 0.5 ? Visibility.Visible : Visibility.Collapsed;

        if (secondaryWidth > 0.5)
        {
            PrimaryFill.CornerRadius = secondaryWidth > 0.5
                ? new CornerRadius(6, 0, 0, 6)
                : new CornerRadius(6);
            SecondaryFill.CornerRadius = primaryWidth > 0.5
                ? new CornerRadius(0, 6, 6, 0)
                : new CornerRadius(6);
        }
        else
        {
            PrimaryFill.CornerRadius = primaryWidth >= innerWidth - 0.5
                ? new CornerRadius(6)
                : new CornerRadius(6, 0, 0, 6);
        }
    }

    private void ApplyFillBrush(Border target, Brush? brush)
    {
        if (brush is null)
        {
            target.Background = null;
            return;
        }

        if (brush is SolidColorBrush solid)
        {
            var baseColor = solid.Color;
            target.Background = new LinearGradientBrush(
                BlendColor(baseColor, 1.18f),
                BlendColor(baseColor, 0.82f),
                new Point(0, 0),
                new Point(0, 1));
            return;
        }

        target.Background = brush;
    }

    private static Color BlendColor(Color color, float factor)
    {
        static byte Clamp(float value) => (byte)Math.Clamp(value, 0, 255);

        return Color.FromArgb(
            color.A,
            Clamp(color.R * factor),
            Clamp(color.G * factor),
            Clamp(color.B * factor));
    }
}
