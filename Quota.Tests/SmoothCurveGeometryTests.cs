using System.Windows;
using Quota.Helpers;
using Xunit;

namespace Quota.Tests;

public class SmoothCurveGeometryTests
{
    [Fact]
    public void ResetCase_AnchorBeforeDrop_HasZeroLengthHandles()
    {
        var points = ValuesToCanvasPoints(90, 95, 100, 0);
        var controls = SmoothCurveGeometry.CreateBezierControlPoints(points);

        var anchorBeforeDrop = points[2];
        var segmentIntoAnchor = controls[1];
        var segmentFromAnchor = controls[2];

        Assert.Equal(anchorBeforeDrop, segmentIntoAnchor.Control2);
        Assert.Equal(anchorBeforeDrop, segmentFromAnchor.Control1);
        Assert.True(segmentIntoAnchor.Control2.Y <= anchorBeforeDrop.Y + 0.001);
    }

    [Fact]
    public void LocalPeak_BeforeDrop_HasZeroLengthHandles()
    {
        var points = ValuesToCanvasPoints(30, 50, 35);
        var controls = SmoothCurveGeometry.CreateBezierControlPoints(points);
        var peak = points[1];

        Assert.Equal(peak, controls[0].Control2);
        Assert.Equal(peak, controls[1].Control1);
    }

    [Fact]
    public void MonotonicRise_KeepsDefaultSmoothing()
    {
        var points = ValuesToCanvasPoints(10, 20, 30, 40);
        var controls = SmoothCurveGeometry.CreateBezierControlPoints(points);

        foreach (var (control1, control2) in controls)
        {
            Assert.NotEqual(control1, points[0]);
            Assert.NotEqual(control2, points[^1]);
        }

        Assert.NotEqual(points[1], controls[0].Control2);
        Assert.NotEqual(points[2], controls[1].Control1);
    }

    [Fact]
    public void DropThenRise_OnlyPeakBeforeDropIsCorner()
    {
        var points = ValuesToCanvasPoints(50, 20, 30);
        var controls = SmoothCurveGeometry.CreateBezierControlPoints(points);
        var peak = points[0];

        Assert.Equal(peak, controls[0].Control1);
        Assert.NotEqual(peak, controls[1].Control1);
    }

    [Fact]
    public void ResetCase_NoOvershootBeforeHundred()
    {
        var points = ValuesToCanvasPoints(94, 97, 100, 0, 5);
        var controls = SmoothCurveGeometry.CreateBezierControlPoints(points);
        var hundred = points[2];

        Assert.Equal(hundred, controls[1].Control2);
        Assert.True(controls[1].Control2.Y >= hundred.Y - 0.001);
    }

    [Fact]
    public void IsValueDropOnCanvas_HigherYMeansLowerValue()
    {
        var higherValue = new Point(0, 10);
        var lowerValue = new Point(1, 40);

        Assert.True(SmoothCurveGeometry.IsValueDropOnCanvas(higherValue, lowerValue));
        Assert.False(SmoothCurveGeometry.IsValueDropOnCanvas(lowerValue, higherValue));
    }

    private static List<Point> ValuesToCanvasPoints(params double[] values)
    {
        const double plotHeight = 100;
        const double axisMax = 100;

        return values
            .Select((value, index) => new Point(
                index,
                plotHeight - plotHeight * value / axisMax))
            .ToList();
    }
}
