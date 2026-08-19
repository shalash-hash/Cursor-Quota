using System.Globalization;

namespace Quota.Localization;

public sealed class LanguageOption
{
    public LanguageOption(string cultureName, string nativeName)
    {
        Culture = CultureInfo.GetCultureInfo(cultureName);
        NativeName = nativeName;
    }

    public CultureInfo Culture { get; }

    public string NativeName { get; }

    public System.Windows.FlowDirection FlowDirection =>
        Culture.TwoLetterISOLanguageName is "ar" or "he"
            ? System.Windows.FlowDirection.RightToLeft
            : System.Windows.FlowDirection.LeftToRight;

    public override string ToString() => NativeName;
}
