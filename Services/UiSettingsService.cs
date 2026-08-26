using System.IO;
using System.Text.Json;

namespace Quota.Services;

public sealed class UiSettingsService
{
    public const double DefaultWindowWidth = 840;
    public const double DefaultWindowHeight = 547;
    public const double MinWindowWidth = 760;
    public const double MinWindowHeight = 420;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public UiSettingsService()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quota");
        Directory.CreateDirectory(settingsDirectory);
        _settingsPath = Path.Combine(settingsDirectory, "ui-settings.json");
    }

    public UiSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new UiSettings();

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<UiSettings>(json, JsonOptions);
            return settings ?? new UiSettings();
        }
        catch
        {
            return new UiSettings();
        }
    }

    public void Save(UiSettings settings)
    {
        var existing = Load();
        var sanitized = new UiSettings
        {
            PercentageDecimalPlaces = Math.Clamp(settings.PercentageDecimalPlaces, 0, 7),
            PreferredLanguage = string.IsNullOrWhiteSpace(settings.PreferredLanguage)
                ? existing.PreferredLanguage
                : settings.PreferredLanguage,
            WindowWidth = ResolveWindowSize(
                settings.WindowWidth ?? existing.WindowWidth,
                MinWindowWidth,
                DefaultWindowWidth),
            WindowHeight = ResolveWindowSize(
                settings.WindowHeight ?? existing.WindowHeight,
                MinWindowHeight,
                DefaultWindowHeight),
            IsDarkMode = settings.IsDarkMode
        };

        var json = JsonSerializer.Serialize(sanitized, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    public static double? SanitizeWindowSize(double? value, double min, double? fallback = null)
    {
        if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            return fallback;

        if (value < min || value > 4000)
            return fallback;

        return value.Value;
    }

    private static double ResolveWindowSize(double? value, double min, double fallback)
        => SanitizeWindowSize(value, min, fallback) ?? fallback;
}
