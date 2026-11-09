using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

    public static readonly DependencyProperty PlanMarkerValueProperty =
        DependencyProperty.Register(
            nameof(PlanMarkerValue),
            typeof(double),
            typeof(GraduatedProgressBar),
            new PropertyMetadata(double.NaN, OnLayoutPropertyChanged));

    public static readonly DependencyProperty PlanMarkerToolTipProperty =
        DependencyProperty.Register(
            nameof(PlanMarkerToolTip),
            typeof(object),
            typeof(GraduatedProgressBar),
            new PropertyMetadata(null, OnPlanMarkerToolTipChanged));

    private const double PlanHitHalfWidth = 36;
    private const double PlanHitHalfHeight = 20;
    private const double PlanPopupCursorGap = 12;

    private Window? _hoverWindow;
    private bool _trackingHover;

    public GraduatedProgressBar()
    {
        InitializeComponent();
        Track.SizeChanged += (_, _) => UpdateFills();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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

    public double PlanMarkerValue
    {
        get => (double)GetValue(PlanMarkerValueProperty);
        set => SetValue(PlanMarkerValueProperty, value);
    }

    public object? PlanMarkerToolTip
    {
        get => GetValue(PlanMarkerToolTipProperty);
        set => SetValue(PlanMarkerToolTipProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GraduatedProgressBar bar)
            bar.UpdateFills();
    }

    private static void OnPlanMarkerToolTipChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not GraduatedProgressBar bar)
            return;

        bar.SetHoverTracking(bar.PlanMarker.Visibility == Visibility.Visible && bar.HasPlanMarkerText());
        bar.RefreshPlanHover();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hoverWindow = Window.GetWindow(this);
        UpdateFills();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SetHoverTracking(false);
        _hoverWindow = null;
    }

    private void OnWindowPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e) =>
        UpdateHover(e.GetPosition(Track));

    private void OnWindowMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => ClosePlanPopup();

    private void OnWindowDeactivated(object? sender, EventArgs e) => ClosePlanPopup();

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

        UpdatePlanMarker(innerWidth, maximum);
    }

    private void UpdatePlanMarker(double innerWidth, double maximum)
    {
        if (double.IsNaN(PlanMarkerValue) || PlanMarkerValue < 0)
        {
            PlanMarker.Visibility = Visibility.Collapsed;
            SetHoverTracking(false);
            return;
        }

        var markerPercent = Math.Clamp(PlanMarkerValue, 0, maximum) / maximum;
        var x = 1 + innerWidth * markerPercent;
        PlanMarker.Margin = new Thickness(Math.Max(0, x - 1), 0, 0, 0);
        PlanMarker.Visibility = Visibility.Visible;
        SetHoverTracking(HasPlanMarkerText());
        RefreshPlanHover();
    }

    private void RefreshPlanHover()
    {
        if (!_trackingHover || _hoverWindow is null)
            return;

        UpdateHover(Mouse.GetPosition(Track));
    }

    private void UpdateHover(Point mouseInTrack)
    {
        if (!HasPlanMarkerText() || PlanMarker.Visibility != Visibility.Visible || PlanMarker.ActualWidth <= 0)
        {
            ClosePlanPopup();
            return;
        }

        var center = PlanMarker.TranslatePoint(
            new Point(PlanMarker.ActualWidth / 2, PlanMarker.ActualHeight / 2),
            Track);
        var inside = Math.Abs(mouseInTrack.X - center.X) <= PlanHitHalfWidth
                     && Math.Abs(mouseInTrack.Y - center.Y) <= PlanHitHalfHeight;
        if (!inside)
        {
            ClosePlanPopup();
            return;
        }

        MovePlanPopup(mouseInTrack, center);
    }

    private void MovePlanPopup(Point mouseInTrack, Point markerCenter)
    {
        PlanMarkerPopupText.Text = PlanMarkerToolTip?.ToString() ?? string.Empty;
        PlanMarkerPopupBorder.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var popupSize = PlanMarkerPopupBorder.DesiredSize;

        var x = mouseInTrack.X - popupSize.Width / 2;
        var y = markerCenter.Y + PlanHitHalfHeight + PlanPopupCursorGap;
        if (_hoverWindow is not null && popupSize.Width > 0 && popupSize.Height > 0)
        {
            var origin = Track.TranslatePoint(new Point(0, 0), _hoverWindow);
            var minX = -origin.X + 4;
            var maxX = _hoverWindow.ActualWidth - origin.X - popupSize.Width - 4;
            x = maxX < minX ? minX : Math.Clamp(x, minX, maxX);

            var belowBottom = origin.Y + y + popupSize.Height;
            if (belowBottom > _hoverWindow.ActualHeight - 4)
                y = markerCenter.Y - PlanHitHalfHeight - popupSize.Height - PlanPopupCursorGap;
        }

        PlanMarkerPopup.HorizontalOffset = x;
        PlanMarkerPopup.VerticalOffset = y;
        PlanMarkerPopup.Placement = PlacementMode.Relative;
        PlanMarkerPopup.PlacementTarget = Track;
        if (!PlanMarkerPopup.IsOpen)
            PlanMarkerPopup.IsOpen = true;
    }

    private bool HasPlanMarkerText() =>
        !string.IsNullOrWhiteSpace(PlanMarkerToolTip?.ToString());

    private void SetHoverTracking(bool enabled)
    {
        if (_hoverWindow is null || _trackingHover == enabled)
        {
            if (!enabled)
                ClosePlanPopup();
            return;
        }

        if (enabled)
        {
            _hoverWindow.PreviewMouseMove += OnWindowPreviewMouseMove;
            _hoverWindow.MouseLeave += OnWindowMouseLeave;
            _hoverWindow.Deactivated += OnWindowDeactivated;
        }
        else
        {
            _hoverWindow.PreviewMouseMove -= OnWindowPreviewMouseMove;
            _hoverWindow.MouseLeave -= OnWindowMouseLeave;
            _hoverWindow.Deactivated -= OnWindowDeactivated;
            ClosePlanPopup();
        }

        _trackingHover = enabled;
    }

    private void ClosePlanPopup()
    {
        if (PlanMarkerPopup.IsOpen)
            PlanMarkerPopup.IsOpen = false;
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
