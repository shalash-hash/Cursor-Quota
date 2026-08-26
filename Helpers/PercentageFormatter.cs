using System.Globalization;
using Quota.Localization;

namespace Quota.Helpers;

public static class PercentageFormatter
{
    public static string Format(double value, int decimalPlaces, CultureInfo culture)
    {
        var sanitizedPlaces = Math.Clamp(decimalPlaces, 0, 7);
        var pattern = sanitizedPlaces == 0
            ? "0"
            : "0." + new string('0', sanitizedPlaces);

        return (value / 100d).ToString("P" + sanitizedPlaces, culture);
    }

    public static string FormatNumber(double value, int decimalPlaces, CultureInfo culture)
    {
        var sanitizedPlaces = Math.Clamp(decimalPlaces, 0, 7);
        var pattern = sanitizedPlaces == 0
            ? "0"
            : "0." + new string('0', sanitizedPlaces);

        return value.ToString(pattern, culture);
    }

    public static string FormatUsd(decimal value, CultureInfo culture)
    {
        return $"${value.ToString("0.00", culture)}";
    }

    public static string FormatDays(int days, ILocalizationService localization)
        => FormatUnit(days, "DaysPattern", localization);

    public static string FormatUnit(int count, string keyPrefix, ILocalizationService localization)
    {
        var culture = localization.CurrentCulture;
        var form = SelectPluralForm(count, culture);
        var key = keyPrefix + form;
        var template = localization.GetString(key);
        if (template == key)
            template = localization.GetString(keyPrefix + "Other");
        return string.Format(culture, template, count);
    }

    private static string SelectPluralForm(int value, CultureInfo culture)
    {
        var absolute = Math.Abs(value);
        var mod10 = absolute % 10;
        var mod100 = absolute % 100;
        var language = culture.TwoLetterISOLanguageName.ToLowerInvariant();
        var fullName = culture.Name.ToLowerInvariant();

        return fullName switch
        {
            "pt-br" => absolute <= 1 ? "One" : "Other",
            _ => language switch
        {
            "ru" or "uk" => mod10 == 1 && mod100 != 11
                ? "One"
                : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14
                    ? "Few"
                    : "Many",
            "pl" => absolute == 1
                ? "One"
                : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14
                    ? "Few"
                    : "Many",
            "cs" => absolute == 1
                ? "One"
                : absolute is >= 2 and <= 4
                    ? "Few"
                    : "Other",
            "ar" => absolute switch
            {
                0 => "Zero",
                1 => "One",
                2 => "Two",
                >= 3 and <= 10 => "Few",
                _ => "Many"
            },
            "fr" => absolute <= 1 ? "One" : "Other",
            "ro" => absolute == 1 ? "One" : "Other",
            _ => absolute == 1 ? "One" : "Other"
        }};
    }
}
