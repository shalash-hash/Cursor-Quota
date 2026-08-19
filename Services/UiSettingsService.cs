using System.IO;
using System.Text.Json;

namespace Quota.Services;

public sealed class UiSettingsService
{
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
        var sanitized = new UiSettings
        {
            PercentageDecimalPlaces = Math.Clamp(settings.PercentageDecimalPlaces, 0, 4),
            PreferredLanguage = string.IsNullOrWhiteSpace(settings.PreferredLanguage)
                ? null
                : settings.PreferredLanguage
        };

        var json = JsonSerializer.Serialize(sanitized, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
