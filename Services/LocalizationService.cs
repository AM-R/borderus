using System.Windows;

namespace Borderus.Services;

internal static class LocalizationService
{
    internal static string Normalize(string? language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";

    internal static void Apply(string? language)
    {
        string code = Normalize(language);
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"/Borderus;component/Resources/Strings.{code}.xaml", UriKind.Relative)
        };
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        if (dictionaries.Count == 0) dictionaries.Add(dictionary);
        else dictionaries[0] = dictionary;
    }

    internal static string Text(string key) =>
        System.Windows.Application.Current.TryFindResource(key) as string ?? key;
}
