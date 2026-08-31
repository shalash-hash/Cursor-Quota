using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Quota.Helpers;

public static class SystemCultureHelper
{
    private const uint MuiLanguageName = 0x8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetUserPreferredUILanguages(
        uint dwFlags,
        out uint pulNumLanguages,
        char[]? pwszLanguagesBuffer,
        ref uint pcchLanguagesBuffer);

    public static CultureInfo GetPrimaryCulture()
    {
        foreach (var cultureName in GetPreferredCultureNames())
        {
            try
            {
                return CultureInfo.GetCultureInfo(cultureName);
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return CultureInfo.CurrentUICulture;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> GetPreferredCultureNames()
    {
        if (!OperatingSystem.IsWindows())
            yield break;

        uint languageCount = 0;
        uint bufferSize = 0;
        if (GetUserPreferredUILanguages(MuiLanguageName, out languageCount, null, ref bufferSize) != 0
            || bufferSize == 0)
        {
            yield break;
        }

        var buffer = new char[bufferSize];
        if (GetUserPreferredUILanguages(MuiLanguageName, out languageCount, buffer, ref bufferSize) != 0)
            yield break;

        foreach (var cultureName in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!string.IsNullOrWhiteSpace(cultureName))
                yield return cultureName;
        }
    }
}
