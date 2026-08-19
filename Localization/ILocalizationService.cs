using System.ComponentModel;
using System.Globalization;

namespace Quota.Localization;

public interface ILocalizationService : INotifyPropertyChanged
{
    IReadOnlyList<LanguageOption> SupportedLanguages { get; }

    LanguageOption SelectedLanguage { get; set; }

    CultureInfo CurrentCulture { get; }

    System.Windows.FlowDirection CurrentFlowDirection { get; }

    string this[string key] { get; }

    string GetString(string key);

    string Format(string key, params object[] args);
}
