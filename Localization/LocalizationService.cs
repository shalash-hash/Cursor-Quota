using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Quota.Resources;
using Quota.Services;

namespace Quota.Localization;

public sealed class LocalizationService : ILocalizationService
{
    private static readonly ResourceManager ResourceManager =
        new("Quota.Resources.Strings", typeof(StringsAnchor).Assembly);

    private static readonly IReadOnlyList<LanguageOption> Languages =
    [
        new("ru", "Русский"),
        new("en", "English"),
        new("de", "Deutsch"),
        new("fr", "Français"),
        new("es", "Español"),
        new("it", "Italiano"),
        new("pt-BR", "Português (Brasil)"),
        new("nl", "Nederlands"),
        new("pl", "Polski"),
        new("uk", "Українська"),
        new("tr", "Türkçe"),
        new("cs", "Čeština"),
        new("ro", "Română"),
        new("hu", "Magyar"),
        new("el", "Ελληνικά"),
        new("ar", "العربية"),
        new("he", "עברית"),
        new("hi", "हिन्दी"),
        new("zh-CN", "中文（简体）"),
        new("zh-TW", "中文（繁體）"),
        new("ja", "日本語"),
        new("ko", "한국어"),
        new("id", "Bahasa Indonesia"),
        new("vi", "Tiếng Việt"),
        new("th", "ไทย")
    ];

    public static LocalizationService Instance { get; private set; } = null!;

    private readonly UiSettingsService _uiSettingsService;
    private readonly UiSettings _uiSettings;
    private LanguageOption _selectedLanguage;

    public LocalizationService(UiSettingsService uiSettingsService)
    {
        _uiSettingsService = uiSettingsService;
        _uiSettings = _uiSettingsService.Load();
        _selectedLanguage = ResolveInitialLanguage(_uiSettings.PreferredLanguage);
        ApplyCulture(_selectedLanguage.Culture);
        Instance = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LanguageOption> SupportedLanguages => Languages;

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (ReferenceEquals(_selectedLanguage, value))
                return;

            _selectedLanguage = value;
            _uiSettings.PreferredLanguage = value.Culture.Name;
            _uiSettingsService.Save(_uiSettings);
            ApplyCulture(value.Culture);
            RaiseAllChanged();
        }
    }

    public CultureInfo CurrentCulture => _selectedLanguage.Culture;

    public System.Windows.FlowDirection CurrentFlowDirection => _selectedLanguage.FlowDirection;

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        return ResourceManager.GetString(key, CurrentCulture) ?? key;
    }

    public string Format(string key, params object[] args)
    {
        return string.Format(CurrentCulture, GetString(key), args);
    }

    public void RefreshBindings()
    {
        RaiseAllChanged();
    }

    private static LanguageOption ResolveInitialLanguage(string? preferredLanguage)
    {
        if (!string.IsNullOrWhiteSpace(preferredLanguage))
        {
            var saved = FindBestMatch(CultureInfo.GetCultureInfo(preferredLanguage));
            if (saved is not null)
                return saved;
        }

        return FindBestMatch(CultureInfo.CurrentUICulture)
            ?? Languages.First(language => language.Culture.Name == "en");
    }

    private static LanguageOption? FindBestMatch(CultureInfo culture)
    {
        var exact = Languages.FirstOrDefault(language =>
            string.Equals(language.Culture.Name, culture.Name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        return culture.Name.ToLowerInvariant() switch
        {
            "zh-hk" or "zh-mo" => Languages.First(language => language.Culture.Name == "zh-TW"),
            "zh-sg" => Languages.First(language => language.Culture.Name == "zh-CN"),
            "pt" or "pt-pt" => Languages.First(language => language.Culture.Name == "pt-BR"),
            _ => Languages.FirstOrDefault(language =>
                string.Equals(
                    language.Culture.TwoLetterISOLanguageName,
                    culture.TwoLetterISOLanguageName,
                    StringComparison.OrdinalIgnoreCase))
        };
    }

    private static void ApplyCulture(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private void RaiseAllChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
