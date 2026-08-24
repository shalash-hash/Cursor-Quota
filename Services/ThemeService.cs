using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;

namespace Quota.Services;

public sealed class ThemeService
{
    private readonly UiSettingsService _uiSettingsService;

    public ThemeService(UiSettingsService uiSettingsService)
    {
        _uiSettingsService = uiSettingsService;
    }

    public bool IsDarkMode { get; private set; }

    public void ApplySavedTheme()
    {
        var settings = _uiSettingsService.Load();
        ApplyTheme(settings.IsDarkMode);
    }

    public void SetDarkMode(bool isDarkMode)
    {
        if (IsDarkMode == isDarkMode)
            return;

        var settings = _uiSettingsService.Load();
        settings.IsDarkMode = isDarkMode;
        _uiSettingsService.Save(settings);
        ApplyTheme(isDarkMode);
    }

    private void ApplyTheme(bool isDarkMode)
    {
        IsDarkMode = isDarkMode;

        var resources = WpfApplication.Current.Resources;
        var palette = isDarkMode ? DarkPalette : LightPalette;

        SetBrushColor(resources, "WindowBackgroundBrush", palette.WindowBackground);
        SetBrushColor(resources, "CardBackgroundBrush", palette.CardBackground);
        SetBrushColor(resources, "AccentBrush", palette.Accent);
        SetBrushColor(resources, "AccentLightBrush", palette.AccentLight);
        SetBrushColor(resources, "PrimaryTextBrush", palette.PrimaryText);
        SetBrushColor(resources, "SecondaryTextBrush", palette.SecondaryText);
        SetBrushColor(resources, "ErrorBrush", palette.Error);
        SetBrushColor(resources, "BorderBrush", palette.Border);
        SetBrushColor(resources, "ProgressTrackBrush", palette.ProgressTrack);
        SetBrushColor(resources, "FirstPartyBrush", palette.FirstParty);
        SetBrushColor(resources, "ApiBrush", palette.Api);
        SetBrushColor(resources, "SpentTodayBrush", palette.SpentToday);
        SetBrushColor(resources, "SpentYesterdayBrush", palette.SpentYesterday);
        SetBrushColor(resources, "ErrorBackgroundBrush", palette.ErrorBackground);
        SetBrushColor(resources, "ErrorBorderBrush", palette.ErrorBorder);
        SetBrushColor(resources, "InputBackgroundBrush", palette.InputBackground);
        SetBrushColor(resources, "InputForegroundBrush", palette.InputForeground);
        SetBrushColor(resources, "InputBorderBrush", palette.InputBorder);
        SetBrushColor(resources, "SwitchTrackOffBrush", palette.SwitchTrackOff);
        SetBrushColor(resources, "SwitchThumbBrush", palette.SwitchThumb);
    }

    private static void SetBrushColor(ResourceDictionary resources, string key, WpfColor color)
    {
        resources[key] = new SolidColorBrush(color);
    }

    private static ThemePalette LightPalette { get; } = new(
        WindowBackground: ColorFromHex("#F3F4F6"),
        CardBackground: ColorFromHex("#FFFFFF"),
        Accent: ColorFromHex("#2563EB"),
        AccentLight: ColorFromHex("#EFF6FF"),
        PrimaryText: ColorFromHex("#111827"),
        SecondaryText: ColorFromHex("#6B7280"),
        Error: ColorFromHex("#DC2626"),
        Border: ColorFromHex("#E5E7EB"),
        ProgressTrack: ColorFromHex("#E5E7EB"),
        FirstParty: ColorFromHex("#6366F1"),
        Api: ColorFromHex("#0EA5E9"),
        SpentToday: ColorFromHex("#059669"),
        SpentYesterday: ColorFromHex("#D97706"),
        ErrorBackground: ColorFromHex("#FEF2F2"),
        ErrorBorder: ColorFromHex("#FECACA"),
        InputBackground: ColorFromHex("#FFFFFF"),
        InputForeground: ColorFromHex("#111827"),
        InputBorder: ColorFromHex("#D1D5DB"),
        SwitchTrackOff: ColorFromHex("#D1D5DB"),
        SwitchThumb: ColorFromHex("#FFFFFF"));

    private static ThemePalette DarkPalette { get; } = new(
        WindowBackground: ColorFromHex("#111827"),
        CardBackground: ColorFromHex("#1F2937"),
        Accent: ColorFromHex("#60A5FA"),
        AccentLight: ColorFromHex("#1E3A5F"),
        PrimaryText: ColorFromHex("#F9FAFB"),
        SecondaryText: ColorFromHex("#9CA3AF"),
        Error: ColorFromHex("#F87171"),
        Border: ColorFromHex("#374151"),
        ProgressTrack: ColorFromHex("#374151"),
        FirstParty: ColorFromHex("#818CF8"),
        Api: ColorFromHex("#38BDF8"),
        SpentToday: ColorFromHex("#34D399"),
        SpentYesterday: ColorFromHex("#FBBF24"),
        ErrorBackground: ColorFromHex("#450A0A"),
        ErrorBorder: ColorFromHex("#991B1B"),
        InputBackground: ColorFromHex("#374151"),
        InputForeground: ColorFromHex("#F9FAFB"),
        InputBorder: ColorFromHex("#4B5563"),
        SwitchTrackOff: ColorFromHex("#4B5563"),
        SwitchThumb: ColorFromHex("#F9FAFB"));

    private static WpfColor ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        var value = Convert.ToUInt32(hex, 16);
        return WpfColor.FromRgb(
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
    }

    private readonly record struct ThemePalette(
        WpfColor WindowBackground,
        WpfColor CardBackground,
        WpfColor Accent,
        WpfColor AccentLight,
        WpfColor PrimaryText,
        WpfColor SecondaryText,
        WpfColor Error,
        WpfColor Border,
        WpfColor ProgressTrack,
        WpfColor FirstParty,
        WpfColor Api,
        WpfColor SpentToday,
        WpfColor SpentYesterday,
        WpfColor ErrorBackground,
        WpfColor ErrorBorder,
        WpfColor InputBackground,
        WpfColor InputForeground,
        WpfColor InputBorder,
        WpfColor SwitchTrackOff,
        WpfColor SwitchThumb);
}
