using System.Globalization;
using Quota.Helpers;
using Xunit;

namespace Quota.Tests;

public class ChartAxisScalerTests
{
    [Fact]
    public void Create_SmallRange_RoundsUpToTwentyWithStepFive()
    {
        var scale = ChartAxisScaler.Create(14);

        Assert.Equal(20, scale.Max);
        Assert.Equal([0, 5, 10, 15, 20], scale.Ticks);
    }

    [Fact]
    public void Create_MediumRange_UsesStepTen()
    {
        var scale = ChartAxisScaler.Create(47);

        Assert.Equal(50, scale.Max);
        Assert.Equal([0, 10, 20, 30, 40, 50], scale.Ticks);
    }

    [Fact]
    public void FormatTick_UsesIntegerLabels()
    {
        Assert.Equal("25", ChartAxisScaler.FormatTick(25, CultureInfo.InvariantCulture));
    }
}
