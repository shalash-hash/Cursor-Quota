using Microsoft.Win32;

namespace Quota.Services;

public class StartupService
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Quota";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
        return key?.GetValue(AppName) is string;
    }

    public void Enable()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу.");

        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true)
            ?? throw new InvalidOperationException("Не удалось открыть ключ реестра автозапуска.");

        key.SetValue(AppName, $"\"{executablePath}\"");
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
