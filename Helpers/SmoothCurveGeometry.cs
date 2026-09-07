using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace Quota.Helpers;

public static class SmoothCurveGeometry
{
    /// <summary>
    /// На Canvas ось Y направлена вниз: падение значения графика = рост Y.
    /// </summary>
    public static bool IsValueDropOnCanvas(Point current, Point next) =>
        next.Y > current.Y;

    public static bool IsPointBeforeDrop(IReadOnlyList<Point> points, int index) =>
        index + 1 < points.Count && IsValueDropOnCanvas(points[index], points[index + 1]);

    /// <summary>
    /// Точка сразу после падения значения (дно reset): входящий сегмент — drop.
    /// </summary>
    public static bool IsPointAfterDrop(IReadOnlyList<Point> points, int index) =>
        index > 0 && IsValueDropOnCanvas(points[index - 1], points[index]);

    public static bool IsCornerPoint(IReadOnlyList<Point> points, int index) =>
        IsPointBeforeDrop(points, index) || IsPointAfterDrop(points, index);

    public static IReadOnlyList<(Point Control1, Point Control2)> CreateBezierControlPoints(
        IReadOnlyList<Point> points)
    {
        if (points.Count < 2)
            return Array.Empty<(Point, Point)>();

        var controls = new (Point Control1, Point Control2)[points.Count - 1];

        for (var index = 0; index < points.Count - 1; index++)
        {
            var current = points[index];
            var next = points[index + 1];
            var previous = index == 0 ? current : points[index - 1];
            var afterNext = index + 2 < points.Count ? points[index + 2] : next;

            var control1 = IsCornerPoint(points, index)
                ? current
                : new Point(
                    current.X + (next.X - previous.X) / 6,
                    current.Y + (next.Y - previous.Y) / 6);

            var control2 = IsCornerPoint(points, index + 1)
                ? next
                : new Point(
                    next.X - (afterNext.X - current.X) / 6,
                    next.Y - (afterNext.Y - current.Y) / 6);

            controls[index] = (control1, control2);
        }

        return controls;
    }

    public static Geometry CreateSegmentGeometry(
        IReadOnlyList<Point> points,
        int segmentIndex,
        IReadOnlyList<(Point Control1, Point Control2)>? controlPoints = null)
    {
        if (segmentIndex < 0 || segmentIndex >= points.Count - 1)
            return Geometry.Empty;

        var controls = controlPoints ?? CreateBezierControlPoints(points);
        var (control1, control2) = controls[segmentIndex];
        var endPoint = points[segmentIndex + 1];

        var figure = new PathFigure
        {
            StartPoint = points[segmentIndex],
            IsClosed = false
        };

        if (points.Count == 2 && segmentIndex == 0)
        {
            figure.Segments.Add(new LineSegment(endPoint, true));
        }
        else
        {
            figure.Segments.Add(new BezierSegment(control1, control2, endPoint, true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    public static Geometry CreateSmoothGeometry(IReadOnlyList<Point> points)
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

        foreach (var (control1, control2) in CreateBezierControlPoints(points))
        {
            var segmentIndex = figure.Segments.Count;
            figure.Segments.Add(new BezierSegment(control1, control2, points[segmentIndex + 1], true));
        }

        return geometry;
    }
}
