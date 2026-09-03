using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Quota.Helpers;
using Quota.Localization;
using Quota.Models;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;
using UserControl = System.Windows.Controls.UserControl;

namespace Quota.Controls;

public partial class UsageHistoryChart : UserControl
{
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(
            nameof(Points),
            typeof(IReadOnlyList<UsageHistoryPoint>),
            typeof(UsageHistoryChart),
            new PropertyMetadata(null, OnChartDataChanged));

    public static readonly DependencyProperty DecimalPlacesProperty =
        DependencyProperty.Register(
            nameof(DecimalPlaces),
            typeof(int),
            typeof(UsageHistoryChart),
            new PropertyMetadata(1, OnChartDataChanged));

    public static readonly DependencyProperty SectionProperty =
        DependencyProperty.Register(
            nameof(Section),
            typeof(UsageHistoryChartSection),
            typeof(UsageHistoryChart),
            new PropertyMetadata(UsageHistoryChartSection.Daily, OnChartDataChanged));

    private const double LeftPadding = 40;
    private const double RightPadding = 12;
    private const double TopPadding = 12;
    private const double BottomPadding = 28;
    private const double TooltipHeadroom = 56;

    private UIElement? _hoverPopup;
    private Rectangle? _activeHitArea;
    private Rect? _hoverPopupBounds;
    private DispatcherTimer? _hideHoverTimer;

    public UsageHistoryChart()
    {
        InitializeComponent();
        ChartCanvas.MouseLeave += (_, _) => HideBarHoverImmediately();
    }

    public IReadOnlyList<UsageHistoryPoint>? Points
    {
        get => (IReadOnlyList<UsageHistoryPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public int DecimalPlaces
    {
        get => (int)GetValue(DecimalPlacesProperty);
        set => SetValue(DecimalPlacesProperty, value);
    }

    public UsageHistoryChartSection Section
    {
        get => (UsageHistoryChartSection)GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    private static void OnChartDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UsageHistoryChart chart)
            chart.Redraw();
    }

    private void OnChartHostSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        HideBarHoverImmediately();
        ChartCanvas.Children.Clear();

        var points = Points;
        if (points is null || points.Count == 0)
            return;

        var width = Math.Max(0, ChartHost.ActualWidth);
        var height = Math.Max(0, ChartHost.ActualHeight);
        ChartCanvas.Width = width;
        ChartCanvas.Height = height;
        if (width <= 1 || height <= 1)
            return;

        var plotHeight = height - TopPadding - BottomPadding;
        var plotWidth = width - LeftPadding - RightPadding;

        if (Section == UsageHistoryChartSection.Daily)
        {
            var maxDaily = Math.Max(points.Max(point => point.DailySpentPercent), 0.1);
            var dailyScale = ChartAxisScaler.Create(maxDaily);
            DrawSection(
                points,
                plotWidth,
                TopPadding,
                plotHeight,
                dailyScale,
                drawBars: true,
                drawLine: false);
            return;
        }

        var maxCumulative = Math.Max(points.Max(point => point.CumulativeUsedPercent), 1);
        var cumulativeScale = ChartAxisScaler.Create(maxCumulative);
        DrawSection(
            points,
            plotWidth,
            TopPadding,
            plotHeight,
            cumulativeScale,
            drawBars: false,
            drawLine: true);
    }

    private void DrawSection(
        IReadOnlyList<UsageHistoryPoint> points,
        double plotWidth,
        double top,
        double plotHeight,
        ChartAxisScaler.Scale scale,
        bool drawBars,
        bool drawLine)
    {
        var accentBrush = GetBrush("AccentBrush");
        var modelsBrush = GetBrush("FirstPartyBrush");
        var apiBrush = GetBrush("ApiBrush");
        var gridBrush = GetBrush("BorderBrush");
        var labelBrush = GetBrush("SecondaryTextBrush");
        var culture = CultureInfo.CurrentCulture;
        var axisMax = Math.Max(scale.Max, 0.001);

        foreach (var tick in scale.Ticks)
        {
            var fraction = tick / axisMax;
            var y = top + plotHeight - fraction * plotHeight;
            ChartCanvas.Children.Add(new Line
            {
                X1 = LeftPadding,
                X2 = LeftPadding + plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1,
                Opacity = 0.55
            });

            var text = new TextBlock
            {
                Text = ChartAxisScaler.FormatTick(tick, culture),
                FontSize = 10,
                Foreground = labelBrush,
                Width = LeftPadding - 4,
                TextAlignment = TextAlignment.Right
            };
            Canvas.SetLeft(text, 0);
            Canvas.SetTop(text, y - 8);
            ChartCanvas.Children.Add(text);
        }

        var bucketWidth = plotWidth / points.Count;
        var barWidth = Math.Max(4, bucketWidth * 0.62);

        if (drawBars)
        {
            for (var index = 0; index < points.Count; index++)
            {
                var point = points[index];
                var centerX = LeftPadding + bucketWidth * index + bucketWidth / 2;
                var modelsHeight = plotHeight * point.DailyModelsPercent / axisMax;
                var apiHeight = plotHeight * point.DailyApiPercent / axisMax;
                var x = centerX - barWidth / 2;
                var totalBarHeight = Math.Max(modelsHeight + apiHeight,
                    point.DailySpentPercent > 0.001 ? plotHeight * point.DailySpentPercent / axisMax : 0);

                if (modelsHeight > 0.5)
                {
                    ChartCanvas.Children.Add(new Rectangle
                    {
                        Width = barWidth,
                        Height = modelsHeight,
                        Fill = modelsBrush,
                        RadiusX = 2,
                        RadiusY = 2,
                        IsHitTestVisible = false
                    });
                    Canvas.SetLeft(ChartCanvas.Children[^1], x);
                    Canvas.SetTop(ChartCanvas.Children[^1], top + plotHeight - modelsHeight - apiHeight);
                }

                if (apiHeight > 0.5)
                {
                    ChartCanvas.Children.Add(new Rectangle
                    {
                        Width = barWidth,
                        Height = apiHeight,
                        Fill = apiBrush,
                        RadiusX = 2,
                        RadiusY = 2,
                        IsHitTestVisible = false
                    });
                    Canvas.SetLeft(ChartCanvas.Children[^1], x);
                    Canvas.SetTop(ChartCanvas.Children[^1], top + plotHeight - apiHeight);
                }

                if (modelsHeight <= 0.5 && apiHeight <= 0.5 && point.DailySpentPercent > 0.001)
                {
                    var totalHeight = plotHeight * point.DailySpentPercent / axisMax;
                    ChartCanvas.Children.Add(new Rectangle
                    {
                        Width = barWidth,
                        Height = totalHeight,
                        Fill = accentBrush,
                        RadiusX = 2,
                        RadiusY = 2,
                        IsHitTestVisible = false
                    });
                    Canvas.SetLeft(ChartCanvas.Children[^1], x);
                    Canvas.SetTop(ChartCanvas.Children[^1], top + plotHeight - totalHeight);
                    totalBarHeight = totalHeight;
                }

                AddBucketLabel(point.Label, centerX, top + plotHeight + 8, labelBrush);
                AddBarHitArea(point, index, LeftPadding + bucketWidth * index, top, bucketWidth, plotHeight, centerX, totalBarHeight);
            }
        }

        if (drawLine && points.Count > 0)
        {
            var linePoints = new PointCollection();
            for (var index = 0; index < points.Count; index++)
            {
                var point = points[index];
                var x = LeftPadding + bucketWidth * index + bucketWidth / 2;
                var y = top + plotHeight - plotHeight * point.CumulativeUsedPercent / axisMax;
                linePoints.Add(new Point(x, y));
            }

            var smoothGeometry = CreateSmoothGeometry(linePoints);
            ChartCanvas.Children.Add(new Path
            {
                Data = smoothGeometry,
                Stroke = accentBrush,
                StrokeThickness = 2.5,
                Fill = Brushes.Transparent,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            });

            foreach (var linePoint in linePoints)
            {
                ChartCanvas.Children.Add(new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = accentBrush,
                    IsHitTestVisible = false
                });
                Canvas.SetLeft(ChartCanvas.Children[^1], linePoint.X - 3);
                Canvas.SetTop(ChartCanvas.Children[^1], linePoint.Y - 3);
            }

            if (!drawBars)
            {
                for (var index = 0; index < points.Count; index++)
                {
                    var centerX = LeftPadding + bucketWidth * index + bucketWidth / 2;
                    AddBucketLabel(points[index].Label, centerX, top + plotHeight + 8, labelBrush);
                }
            }
        }
    }

    private void AddBarHitArea(
        UsageHistoryPoint point,
        int index,
        double left,
        double top,
        double width,
        double height,
        double centerX,
        double barHeight)
    {
        var hitTop = Math.Max(0, top - TooltipHeadroom);
        var hitHeight = height + (top - hitTop);

        var hitArea = new Rectangle
        {
            Width = width,
            Height = hitHeight,
            Fill = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Tag = index
        };

        Canvas.SetLeft(hitArea, left);
        Canvas.SetTop(hitArea, hitTop);
        Panel.SetZIndex(hitArea, 100);

        hitArea.MouseEnter += (_, _) =>
        {
            CancelScheduledHide();
            _activeHitArea = hitArea;
            ShowBarHover(point, centerX, top, top + height - barHeight);
        };

        hitArea.MouseLeave += (_, _) => ScheduleHideBarHover();

        ChartCanvas.Children.Add(hitArea);
    }

    private void ShowBarHover(UsageHistoryPoint point, double centerX, double plotTop, double barTop)
    {
        if (_hoverPopup is not null)
            ChartCanvas.Children.Remove(_hoverPopup);

        var culture = LocalizationService.Instance.CurrentCulture;
        var localization = LocalizationService.Instance;
        var digits = QuotaMonetaryHelper.DisplayDecimalPlaces;
        var primaryBrush = GetBrush("PrimaryTextBrush");
        var secondaryBrush = GetBrush("SecondaryTextBrush");
        var usdBrush = GetBrush("SpentYesterdayBrush");

        var content = new StackPanel();
        var label = string.IsNullOrWhiteSpace(point.TooltipLabel) ? point.Label : point.TooltipLabel;
        var totalPercent = PercentageFormatter.Format(point.DailySpentPercent, digits, culture);
        var totalUsd = point.DailyTotalSpentUsd is not null
            ? QuotaMonetaryHelper.FormatUsd(point.DailyTotalSpentUsd.Value, culture)
            : "—";

        content.Children.Add(CreateTooltipLine(
            $"{label}: ",
            totalPercent,
            totalUsd,
            primaryBrush,
            usdBrush,
            fontSize: 11,
            fontWeight: FontWeights.SemiBold));

        if (point.DailyModelsPercent > 0.001 || point.DailyApiPercent > 0.001)
        {
            var modelsPercent = PercentageFormatter.Format(point.DailyModelsPercent, digits, culture);
            var apiPercent = PercentageFormatter.Format(point.DailyApiPercent, digits, culture);
            var modelsAmount = point.DailyModelsSpentUsd is not null
                ? QuotaMonetaryHelper.FormatUsd(point.DailyModelsSpentUsd.Value, culture)
                : "—";
            var apiAmount = point.DailyApiSpentUsd is not null
                ? QuotaMonetaryHelper.FormatUsd(point.DailyApiSpentUsd.Value, culture)
                : "—";

            content.Children.Add(CreateBreakdownTooltipLine(
                localization["StatisticsLegendModels"],
                modelsPercent,
                modelsAmount,
                localization["StatisticsLegendApi"],
                apiPercent,
                apiAmount,
                secondaryBrush,
                usdBrush));
        }

        if (point.CumulativeSpentUsd is not null)
        {
            var cumulativePercent = PercentageFormatter.Format(point.CumulativeUsedPercent, digits, culture);
            var cumulativeUsd = QuotaMonetaryHelper.FormatUsd(point.CumulativeSpentUsd.Value, culture);

            content.Children.Add(CreateTooltipLine(
                $"{localization["StatisticsCumulativeLabel"]}: ",
                cumulativePercent,
                cumulativeUsd,
                secondaryBrush,
                usdBrush,
                fontSize: 10,
                margin: new Thickness(0, 2, 0, 0)));
        }

        var popup = new Border
        {
            Background = GetBrush("CardBackgroundBrush"),
            BorderBrush = GetBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 5, 8, 5),
            Child = content,
            IsHitTestVisible = false
        };

        popup.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var popupWidth = popup.DesiredSize.Width;
        var popupHeight = popup.DesiredSize.Height;
        var canvasWidth = ChartCanvas.ActualWidth;
        var left = Math.Clamp(centerX - popupWidth / 2, 2, Math.Max(2, canvasWidth - popupWidth - 2));
        var tooltipTop = Math.Max(plotTop + 2, barTop - popupHeight - 6);

        Canvas.SetLeft(popup, left);
        Canvas.SetTop(popup, tooltipTop);
        Panel.SetZIndex(popup, 1000);
        ChartCanvas.Children.Add(popup);
        _hoverPopup = popup;
        _hoverPopupBounds = new Rect(left, tooltipTop, popupWidth, popupHeight);
    }

    private static TextBlock CreateTooltipLine(
        string prefix,
        string percentText,
        string usdText,
        Brush textBrush,
        Brush usdBrush,
        double fontSize,
        FontWeight? fontWeight = null,
        Thickness? margin = null)
    {
        var block = new TextBlock
        {
            FontSize = fontSize,
            Margin = margin ?? new Thickness(0)
        };

        if (fontWeight is not null)
            block.FontWeight = fontWeight.Value;

        block.Inlines.Add(new Run(prefix) { Foreground = textBrush });
        block.Inlines.Add(new Run(percentText) { Foreground = textBrush });
        AppendUsdInParens(block, usdText, textBrush, usdBrush);
        return block;
    }

    private static TextBlock CreateBreakdownTooltipLine(
        string modelsLabel,
        string modelsPercent,
        string modelsUsd,
        string apiLabel,
        string apiPercent,
        string apiUsd,
        Brush textBrush,
        Brush usdBrush)
    {
        var block = new TextBlock
        {
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0)
        };

        block.Inlines.Add(new Run($"{modelsLabel} ") { Foreground = textBrush });
        block.Inlines.Add(new Run(modelsPercent) { Foreground = textBrush });
        AppendUsdInParens(block, modelsUsd, textBrush, usdBrush);
        block.Inlines.Add(new Run($", {apiLabel} ") { Foreground = textBrush });
        block.Inlines.Add(new Run(apiPercent) { Foreground = textBrush });
        AppendUsdInParens(block, apiUsd, textBrush, usdBrush);
        return block;
    }

    private static void AppendUsdInParens(
        TextBlock block,
        string usdText,
        Brush textBrush,
        Brush usdBrush)
    {
        block.Inlines.Add(new Run(" (") { Foreground = textBrush });
        block.Inlines.Add(new Run(usdText)
        {
            Foreground = usdBrush,
            FontWeight = FontWeights.SemiBold
        });
        block.Inlines.Add(new Run(")") { Foreground = textBrush });
    }

    private void ScheduleHideBarHover()
    {
        CancelScheduledHide();
        _hideHoverTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(60)
        };
        _hideHoverTimer.Tick += (_, _) =>
        {
            CancelScheduledHide();
            if (ShouldKeepHoverVisible())
                return;

            HideBarHoverImmediately();
        };
        _hideHoverTimer.Start();
    }

    private void CancelScheduledHide()
    {
        if (_hideHoverTimer is null)
            return;

        _hideHoverTimer.Stop();
        _hideHoverTimer = null;
    }

    private bool ShouldKeepHoverVisible()
    {
        var position = Mouse.GetPosition(ChartCanvas);

        if (_activeHitArea is not null && IsPointInside(_activeHitArea, position))
            return true;

        if (_hoverPopupBounds is { } popupBounds && popupBounds.Contains(position))
            return true;

        return false;
    }

    private static bool IsPointInside(FrameworkElement element, Point position)
    {
        var left = Canvas.GetLeft(element);
        var top = Canvas.GetTop(element);
        if (double.IsNaN(left))
            left = 0;
        if (double.IsNaN(top))
            top = 0;

        return position.X >= left
            && position.X <= left + element.ActualWidth
            && position.Y >= top
            && position.Y <= top + element.ActualHeight;
    }

    private void HideBarHoverImmediately()
    {
        CancelScheduledHide();
        _activeHitArea = null;
        _hoverPopupBounds = null;

        if (_hoverPopup is null)
            return;

        ChartCanvas.Children.Remove(_hoverPopup);
        _hoverPopup = null;
    }

    private void AddBucketLabel(string label, double centerX, double top, Brush brush)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = brush,
            TextAlignment = TextAlignment.Center,
            Width = 48,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(text, centerX - 24);
        Canvas.SetTop(text, top);
        ChartCanvas.Children.Add(text);
    }

    private static Geometry CreateSmoothGeometry(PointCollection points)
    {
        var figure = new PathFigure { StartPoint = points[0] };
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        if (points.Count == 1)
            return geometry;

        if (points.Count == 2)
        {
            figure.Segments.Add(new LineSegment(points[1], true));
            return geometry;
        }

        for (var index = 0; index < points.Count - 1; index++)
        {
            var current = points[index];
            var next = points[index + 1];
            var previous = index == 0 ? current : points[index - 1];
            var afterNext = index + 2 < points.Count ? points[index + 2] : next;

            var control1 = new Point(
                current.X + (next.X - previous.X) / 6,
                current.Y + (next.Y - previous.Y) / 6);
            var control2 = new Point(
                next.X - (afterNext.X - current.X) / 6,
                next.Y - (afterNext.Y - current.Y) / 6);

            figure.Segments.Add(new BezierSegment(control1, control2, next, true));
        }

        return geometry;
    }

    private static Brush GetBrush(string resourceKey)
    {
        if (Application.Current?.TryFindResource(resourceKey) is Brush brush)
            return brush;

        return Brushes.Gray;
    }
}
