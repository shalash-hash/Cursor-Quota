using System.Globalization;

namespace Quota.Helpers;

public static class ChartAxisScaler
{
    public sealed record Scale(double Max, IReadOnlyList<double> Ticks);

    public static Scale Create(double dataMax, int targetTickCount = 5)
    {
        if (dataMax <= 0)
            dataMax = 1;

        var roughStep = dataMax / Math.Max(1, targetTickCount - 1);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
        var normalized = roughStep / magnitude;

        var niceNormalized = normalized switch
        {
            <= 1.5 => 1,
            <= 3 => 2,
            <= 7 => 5,
            _ => 10
        };

        var step = niceNormalized * magnitude;
        if (step < 1 && dataMax >= 5)
            step = 1;

        var axisMax = Math.Ceiling(dataMax / step) * step;
        if (axisMax <= dataMax)
            axisMax += step;

        if (axisMax - dataMax < step * 0.25)
            axisMax += step;

        var ticks = new List<double>();
        for (var tick = 0d; tick <= axisMax + step * 0.001; tick += step)
            ticks.Add(tick);

        return new Scale(axisMax, ticks);
    }

    public static string FormatTick(double value, CultureInfo culture)
    {
        if (Math.Abs(value - Math.Round(value)) < 0.001)
            return ((int)Math.Round(value)).ToString(culture);

        return value.ToString("0.#", culture);
    }
}
